namespace Phonepads.Core;

/// <summary>
/// What a mapping drives. The two are different devices with different reach: an Xbox pad
/// works in any Windows game; a Wii Remote carries motion but only emulators understand it.
/// </summary>
public enum PadBackend
{
    /// <summary>A virtual Xbox 360 controller via ViGEmBus. Works with Steam and everything else.</summary>
    XInput,

    /// <summary>
    /// A motion controller served over the DSU (cemuhook) protocol, which Dolphin turns into
    /// an emulated Wii Remote with MotionPlus. Buttons, sticks and inertial data all travel
    /// this way; nothing outside an emulator can read it.
    /// </summary>
    WiiRemote,
}

public static class PadBackendInfo
{
    public static string Label(PadBackend backend) => backend switch
    {
        PadBackend.WiiRemote => "Wii Remote",
        _ => "Xbox controller",
    };
}

/// <summary>What a schema control drives on the virtual pad.</summary>
public enum PadTarget
{
    None,
    LeftStick,
    RightStick,
    LeftStickX,
    LeftStickY,
    RightStickX,
    RightStickY,
    LeftTrigger,
    RightTrigger,
    Dpad,
    A,
    B,
    X,
    Y,
    LeftBumper,
    RightBumper,
    Back,
    Start,
    LeftThumbClick,
    RightThumbClick,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    Guide,
}

/// <summary>Per-control feel adjustments (MAP-3).</summary>
public sealed record AxisTuning
{
    public static readonly AxisTuning Default = new();

    public bool InvertX { get; init; }
    public bool InvertY { get; init; }

    /// <summary>Fraction of travel ignored around centre, in [0, 1).</summary>
    public double Deadzone { get; init; }

    /// <summary>Multiplier applied after the deadzone; 1.0 leaves the input untouched.</summary>
    public double Sensitivity { get; init; } = 1.0;
}

/// <summary>One control's assignment. An unmapped control is allowed and simply does nothing.</summary>
public sealed record ControlMapping
{
    public required PadTarget Target { get; init; }
    public AxisTuning Tuning { get; init; } = AxisTuning.Default;
}

/// <summary>
/// How a schema's controls translate to gamepad inputs. A mapping belongs to its schema and
/// is shared by every player who picks that schema (MAP-2) — there are no per-player overrides.
/// </summary>
public sealed record Mapping
{
    public required string SchemaId { get; init; }
    public required IReadOnlyDictionary<string, ControlMapping> Controls { get; init; }

    /// <summary>Which device the mapping produces. Motion only reaches the game on a Wii Remote.</summary>
    public PadBackend Backend { get; init; } = PadBackend.XInput;

    public static Mapping Empty(string schemaId) => new()
    {
        SchemaId = schemaId,
        Controls = new Dictionary<string, ControlMapping>(StringComparer.Ordinal),
    };

    public ControlMapping? For(string controlId) =>
        Controls.TryGetValue(controlId, out var mapping) ? mapping : null;

    /// <summary>
    /// Targets driven by more than one control, and controls with no assignment. Surfaced
    /// as warnings rather than errors — overlapping targets are legal, just rarely intended.
    /// Motion controls are never "unmapped": they have no pad target, the backend consumes them.
    /// </summary>
    public (IReadOnlyList<string> UnmappedControls, IReadOnlyList<PadTarget> SharedTargets) Review(Schema schema)
    {
        var unmapped = schema.Controls
            .Where(c => !c.IsMotion && For(c.Id) is null or { Target: PadTarget.None })
            .Select(c => c.Id)
            .ToList();

        var shared = schema.Controls
            .Select(c => For(c.Id)?.Target ?? PadTarget.None)
            .Where(t => t != PadTarget.None)
            .GroupBy(t => t)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        return (unmapped, shared);
    }
}
