using Phonepads.Protocol;

namespace Phonepads.Core;

public enum ControlType
{
    Joystick,
    Button,
    Gyro,
}

/// <summary>How a joystick or gyro reports its value; drives which targets make sense.</summary>
public enum ControlMode
{
    Full,
    XOnly,
    YOnly,
    Dpad,
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

    public ControlDto ToDto() => new()
    {
        Type = Type switch
        {
            ControlType.Joystick => "joystick",
            ControlType.Button => "button",
            _ => "gyro",
        },
        Id = Id,
        Label = Label,
        Mode = Mode switch
        {
            ControlMode.XOnly => "x",
            ControlMode.YOnly => "y",
            ControlMode.Dpad => "dpad",
            // "full" is the default; sending it for a button would be meaningless.
            _ => Type == ControlType.Button ? null : "full",
        },
        Zone = Zone switch
        {
            ControlZone.Left => "left",
            ControlZone.Right => "right",
            ControlZone.ShoulderLeft => "shoulder-left",
            ControlZone.ShoulderRight => "shoulder-right",
            ControlZone.Aux => "aux",
            _ => null,
        },
        Size = Size switch
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
    public required string Id { get; init; }
    public required string Name { get; init; }
    public SchemaOrientation Orientation { get; init; } = SchemaOrientation.Auto;
    public required IReadOnlyList<SchemaControl> Controls { get; init; }

    /// <summary>Bundled presets are read-only; editing one produces a copy (SCHEMA-1).</summary>
    public bool IsPreset { get; init; }

    /// <summary>True when any control needs motion sensors, which not every phone has.</summary>
    public bool RequiresGyro => Controls.Any(c => c.Type == ControlType.Gyro);

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

        if (Controls.Count is < 1 or > 16)
            problems.Add($"A schema needs 1 to 16 controls; '{Name}' has {Controls.Count}.");

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

        return problems;
    }

    private static bool IsKebabCase(string value) =>
        value.Length > 0
        && value[0] is >= 'a' and <= 'z'
        && value[^1] != '-'
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
