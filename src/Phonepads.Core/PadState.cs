namespace Phonepads.Core;

[Flags]
public enum PadButtons
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    LeftBumper = 1 << 4,
    RightBumper = 1 << 5,
    Back = 1 << 6,
    Start = 1 << 7,
    LeftThumb = 1 << 8,
    RightThumb = 1 << 9,
    DpadUp = 1 << 10,
    DpadDown = 1 << 11,
    DpadLeft = 1 << 12,
    DpadRight = 1 << 13,
}

/// <summary>
/// A complete virtual gamepad state, in Xbox conventions: thumbstick axes are signed
/// 16-bit with <b>y positive upwards</b>, triggers are 0-255. The phone's axes are
/// [-1, 1] with y positive downwards, so <see cref="MappingEngine"/> flips y on the way in.
/// </summary>
public readonly record struct PadState
{
    public short LeftStickX { get; init; }
    public short LeftStickY { get; init; }
    public short RightStickX { get; init; }
    public short RightStickY { get; init; }
    public byte LeftTrigger { get; init; }
    public byte RightTrigger { get; init; }
    public PadButtons Buttons { get; init; }

    /// <summary>Everything centred and released — what a paused or dropped player's pad holds.</summary>
    public static PadState Neutral => default;

    public bool IsPressed(PadButtons button) => (Buttons & button) != 0;

    /// <summary>Converts an axis in [-1, 1] to the signed 16-bit range a thumbstick uses.</summary>
    public static short ToAxis(double value)
    {
        var clamped = Math.Clamp(value, -1d, 1d);
        // Negative and positive ranges are asymmetric (-32768..32767); scale by the
        // magnitude available in each direction so full deflection reaches the end.
        var scaled = clamped < 0 ? clamped * 32768d : clamped * 32767d;
        return (short)Math.Round(scaled);
    }

    /// <summary>Converts a value in [0, 1] to the 8-bit range a trigger uses.</summary>
    public static byte ToTrigger(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0d, 1d) * 255d);
}
