using System.Text.Json;

namespace Phonepads.Protocol;

/// <summary>Which shape a control's value arrived in.</summary>
public enum ControlValueKind
{
    None,
    /// <summary>Joystick or gyro: one or both axes in [-1, 1], y positive downwards.</summary>
    Axes,
    /// <summary>Joystick in dpad mode: one of the eight screen directions, or centred.</summary>
    Dpad,
    /// <summary>Button: pressed or not.</summary>
    Button,
    /// <summary>Raw touch or touchpad: every finger that is down, possibly none.</summary>
    Touches,
}

/// <summary>
/// One finger on a raw surface or touchpad. X and Y are 0–1 from the surface's left / top
/// edge. Id is the finger's slot (0–9), stable from touch-down to lift-off but reused after.
/// </summary>
public readonly record struct TouchPoint(int Id, double X, double Y);

/// <summary>Screen directions as sent by the phone. "c" means centred/released.</summary>
public enum DpadDirection
{
    Center,
    Up,
    UpRight,
    Right,
    DownRight,
    Down,
    DownLeft,
    Left,
    UpLeft,
}

/// <summary>
/// One control's value inside an input frame. The protocol sends four different JSON
/// shapes under the same key, so this is the union of them.
/// </summary>
public readonly record struct ControlValue
{
    public ControlValueKind Kind { get; init; }

    /// <summary>Horizontal axis in [-1, 1]. Only meaningful when <see cref="Kind"/> is Axes.</summary>
    public double X { get; init; }

    /// <summary>Vertical axis in [-1, 1], positive downwards. Only meaningful when Kind is Axes.</summary>
    public double Y { get; init; }

    public bool Pressed { get; init; }

    public DpadDirection Dpad { get; init; }

    /// <summary>Fingers down on a raw surface or touchpad. Empty unless Kind is Touches.</summary>
    public IReadOnlyList<TouchPoint> Touches
    {
        // A backing field rather than an initializer: default(ControlValue) must read as empty too.
        get => _touches ?? [];
        init => _touches = value;
    }

    private readonly IReadOnlyList<TouchPoint>? _touches;

    public static ControlValue Axes(double x, double y) =>
        new() { Kind = ControlValueKind.Axes, X = x, Y = y };

    public static ControlValue Button(bool pressed) =>
        new() { Kind = ControlValueKind.Button, Pressed = pressed };

    public static ControlValue DpadAt(DpadDirection direction) =>
        new() { Kind = ControlValueKind.Dpad, Dpad = direction };

    public static ControlValue Fingers(IReadOnlyList<TouchPoint> touches) =>
        new() { Kind = ControlValueKind.Touches, Touches = touches };

    /// <summary>
    /// Reads a control value from its JSON form: an object for axes, a string for a dpad
    /// code, a boolean for a button, an array of fingers for raw touch and touchpads.
    /// Anything else yields <see cref="ControlValueKind.None"/>.
    /// </summary>
    public static ControlValue FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => Button(element.GetBoolean()),
        JsonValueKind.String => DpadAt(ParseDpad(element.GetString())),
        JsonValueKind.Object => Axes(ReadAxis(element, "x"), ReadAxis(element, "y")),
        JsonValueKind.Array => Fingers(ReadTouches(element)),
        _ => default,
    };

    /// <summary>Reads a finger list, skipping entries that are not <c>{ id, x, y }</c>.</summary>
    private static List<TouchPoint> ReadTouches(JsonElement array)
    {
        var touches = new List<TouchPoint>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.Number
                || !id.TryGetInt32(out var slot))
            {
                continue;
            }

            touches.Add(new TouchPoint(slot, ReadAxis(item, "x"), ReadAxis(item, "y")));
        }

        return touches;
    }

    private static double ReadAxis(JsonElement element, string name) =>
        element.TryGetProperty(name, out var axis) && axis.ValueKind == JsonValueKind.Number
            ? axis.GetDouble()
            : 0d;

    public static DpadDirection ParseDpad(string? code) => code switch
    {
        "u" => DpadDirection.Up,
        "ur" => DpadDirection.UpRight,
        "r" => DpadDirection.Right,
        "dr" => DpadDirection.DownRight,
        "d" => DpadDirection.Down,
        "dl" => DpadDirection.DownLeft,
        "l" => DpadDirection.Left,
        "ul" => DpadDirection.UpLeft,
        _ => DpadDirection.Center,
    };

    /// <summary>
    /// Maps a dpad direction to a unit vector in the phone's screen coordinates
    /// (y positive downwards), matching the service's own dpadToVector helper.
    /// </summary>
    public static (double X, double Y) DpadToVector(DpadDirection direction)
    {
        const double D = 0.70710678118654752; // 1 / sqrt(2)
        return direction switch
        {
            DpadDirection.Up => (0, -1),
            DpadDirection.UpRight => (D, -D),
            DpadDirection.Right => (1, 0),
            DpadDirection.DownRight => (D, D),
            DpadDirection.Down => (0, 1),
            DpadDirection.DownLeft => (-D, D),
            DpadDirection.Left => (-1, 0),
            DpadDirection.UpLeft => (-D, -D),
            _ => (0, 0),
        };
    }
}
