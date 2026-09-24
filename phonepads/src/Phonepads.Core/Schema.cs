using Phonepads.Protocol;

namespace Phonepads.Core;

public enum ControlType
{
    Joystick,
    Button,
    /// <summary>Tilt as a virtual joystick — the phone does the maths.</summary>
    Gyro,
    /// <summary>
    /// The raw inertial sensors, uncalibrated, at sensor rate. For backends that do their
    /// own motion processing — in practice, emulating a Wii Remote.
    /// </summary>
    Motion,
    /// <summary>
    /// An analog trigger reporting 0..1. Only a real gamepad bridged through the phone has
    /// one; a touch layout cannot declare it, so it never goes to the service.
    /// </summary>
    Trigger,
}

/// <summary>How a joystick or gyro reports its value; drives which targets make sense.</summary>
public enum ControlMode
{
    /// <summary>A round pad reporting {x, y}; the knob springs back to a fixed centre.</summary>
    Full,
    XOnly,
    YOnly,
    Dpad,
    /// <summary>
    /// A square pad reporting {x, y} with no fixed centre: wherever the thumb lands is zero,
    /// and the drag may leave the pad. Same value shape as Full, so mappings never notice.
    /// Meant for aiming and cameras; walking is usually still Full.
    /// </summary>
    Relative,
}

public enum ControlZone
{
    Unspecified,
    Left,
    Right,
    ShoulderLeft,
    ShoulderRight,
    Aux,
}

public enum ControlSize
{
    Unspecified,
    Small,
    Medium,
    Large,
}

public enum SchemaOrientation
{
    Auto,
    Landscape,
    Portrait,
}

/// <summary>One on-screen control the phone will render.</summary>
public sealed record SchemaControl
{
    public required string Id { get; init; }
    public required ControlType Type { get; init; }
    public string? Label { get; init; }
    public ControlMode Mode { get; init; } = ControlMode.Full;
    public ControlZone Zone { get; init; } = ControlZone.Unspecified;
    public ControlSize Size { get; init; } = ControlSize.Unspecified;

    /// <summary>Degrees of tilt for full deflection. Gyro controls only.</summary>
    public int? Range { get; init; }

    /// <summary>True when the control reports a single boolean rather than axes.</summary>
    public bool IsBoolean => Type == ControlType.Button;

    /// <summary>True when the control reports one of the eight screen directions.</summary>
    public bool IsDpad => Type == ControlType.Joystick && Mode == ControlMode.Dpad;

    /// <summary>True for the raw sensor stream, which arrives outside input frames and has no pad target.</summary>
    public bool IsMotion => Type == ControlType.Motion;

    /// <summary>True when the control needs the phone's motion sensors at all.</summary>
    public bool NeedsSensors => Type is ControlType.Gyro or ControlType.Motion;

    /// <summary>True for the analog trigger of a real gamepad, which only exists off-wire.</summary>
    public bool IsTrigger => Type == ControlType.Trigger;

    public ControlDto ToDto() => new()
    {
        Type = Type switch
        {
            ControlType.Joystick => "joystick",
            ControlType.Button or ControlType.Trigger => "button",
            ControlType.Motion => "motion",
            _ => "gyro",
        },
        Id = Id,
        Label = Label,
        Mode = Mode switch
        {
            ControlMode.XOnly => "x",
            ControlMode.YOnly => "y",
            ControlMode.Dpad => "dpad",
            ControlMode.Relative => "relative",
            // "full" is the default; it means nothing for a button or the sensor stream.
            _ => Type is ControlType.Button or ControlType.Trigger or ControlType.Motion ? null : "full",
        },
        // Sensors take no space in the layout, so hints would only confuse the phone.
        Zone = IsMotion ? null : Zone switch
        {
            ControlZone.Left => "left",
            ControlZone.Right => "right",
            ControlZone.ShoulderLeft => "shoulder-left",
            ControlZone.ShoulderRight => "shoulder-right",
            ControlZone.Aux => "aux",
            _ => null,
        },
        Size = IsMotion ? null : Size switch
        {
            ControlSize.Small => "small",
            ControlSize.Medium => "medium",
            ControlSize.Large => "large",
            _ => null,
        },
        Range = Type == ControlType.Gyro ? Range : null,
    };
}

/// <summary>
/// A phone-side control layout. Order matters: the service treats it as importance and
/// lays the controls out accordingly.
/// </summary>
public sealed record Schema
{
    /// <summary>
    /// The id the service reserves for a real controller paired with the phone. It is never
    /// declared in a config — "allowPhysicalGamepad" offers it — but a player who picks it
    /// reports this id, and no touch layout may use it.
    /// </summary>
    public const string PhysicalGamepadId = "physical-gamepad";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public SchemaOrientation Orientation { get; init; } = SchemaOrientation.Auto;
    public required IReadOnlyList<SchemaControl> Controls { get; init; }

    /// <summary>Bundled presets are read-only; editing one produces a copy (SCHEMA-1).</summary>
    public bool IsPreset { get; init; }

    /// <summary>
    /// True for the built-in description of a bridged real gamepad. It is offered with a
    /// config flag rather than sent as a layout, and the phone decides its control set.
    /// </summary>
    public bool IsPhysicalGamepad => Id == PhysicalGamepadId;

    /// <summary>True when any control needs motion sensors, which not every phone has.</summary>
    public bool RequiresGyro => Controls.Any(c => c.NeedsSensors);

    /// <summary>True when the schema streams raw inertial samples.</summary>
    public bool HasMotion => Controls.Any(c => c.IsMotion);

    public SchemaDto ToDto() => new()
    {
        Id = Id,
        Name = Name,
        Orientation = Orientation switch
        {
            SchemaOrientation.Landscape => "landscape",
            SchemaOrientation.Portrait => "portrait",
            _ => "auto",
        },
        Controls = Controls.Select(c => c.ToDto()).ToList(),
    };

    /// <summary>
    /// Checks the constraints the service enforces, so a bad schema is caught before the
    /// setup call consumes the single-use code.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        // The real-gamepad description never goes on the wire, so the wire rules do not apply.
        if (IsPhysicalGamepad) return problems;

        if (Controls.Count is < 1 or > 16)
            problems.Add($"A schema needs 1 to 16 controls; '{Name}' has {Controls.Count}.");

        foreach (var control in Controls.Where(c => c.IsTrigger))
            problems.Add($"Control '{control.Id}' is an analog trigger, which only a real gamepad has; a phone layout cannot declare one.");

        var duplicates = Controls
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var duplicate in duplicates)
            problems.Add($"Control id '{duplicate}' appears more than once in '{Name}'.");

        foreach (var control in Controls.Where(c => !IsKebabCase(c.Id)))
            problems.Add($"Control id '{control.Id}' must be kebab-case.");

        if (!IsKebabCase(Id))
            problems.Add($"Schema id '{Id}' must be kebab-case.");

        // The service allows one of each sensor control per schema.
        if (Controls.Count(c => c.Type == ControlType.Gyro) > 1)
            problems.Add($"'{Name}' has more than one tilt control; the service allows one.");
        if (Controls.Count(c => c.IsMotion) > 1)
            problems.Add($"'{Name}' has more than one motion control; the service allows one.");

        return problems;
    }

    private static bool IsKebabCase(string value) =>
        value.Length > 0
        && value[0] is >= 'a' and <= 'z'
        && value[^1] != '-'
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
