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
}

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
/// One control's value inside an input frame. The protocol sends three different JSON
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

    public static ControlValue Axes(double x, double y) =>
        new() { Kind = ControlValueKind.Axes, X = x, Y = y };

    public static ControlValue Button(bool pressed) =>
        new() { Kind = ControlValueKind.Button, Pressed = pressed };

    public static ControlValue DpadAt(DpadDirection direction) =>
        new() { Kind = ControlValueKind.Dpad, Dpad = direction };

    /// <summary>
    /// Reads a control value from its JSON form: an object for axes, a string for a dpad
    /// code, a boolean for a button. Anything else yields <see cref="ControlValueKind.None"/>.
    /// </summary>
    public static ControlValue FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => Button(element.GetBoolean()),
        JsonValueKind.String => DpadAt(ParseDpad(element.GetString())),
        JsonValueKind.Object => Axes(ReadAxis(element, "x"), ReadAxis(element, "y")),
        _ => default,
    };

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
