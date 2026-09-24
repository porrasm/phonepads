namespace Phonepads.Core;

/// <summary>
/// Where each Wii Remote and Nunchuk control lives on the pad model when a mapping's backend
/// is <see cref="PadBackend.WiiRemote"/>. The DSU protocol only knows DualShock-shaped pads,
/// so a Wii button has to ride on one of those buttons; this is the one place that decides
/// which. The Wii presets map to these targets and the generated Dolphin profile reads them
/// back into Wii buttons, so the two can never disagree.
/// </summary>
public static class WiiLayout
{
    public const PadTarget A = PadTarget.A;
    public const PadTarget B = PadTarget.B;
    public const PadTarget One = PadTarget.X;
    public const PadTarget Two = PadTarget.Y;
    public const PadTarget Plus = PadTarget.Start;
    public const PadTarget Minus = PadTarget.Back;
    public const PadTarget Home = PadTarget.Guide;
    public const PadTarget Dpad = PadTarget.Dpad;

    /// <summary>Re-zeroes gyro pointing, which drifts because yaw has no gravity reference.</summary>
    public const PadTarget Recenter = PadTarget.RightThumbClick;

    public const PadTarget NunchukStick = PadTarget.LeftStick;
    public const PadTarget NunchukC = PadTarget.LeftBumper;
    public const PadTarget NunchukZ = PadTarget.LeftTrigger;

    /// <summary>Every target a Wii mapping can legitimately use, for validation and the profile.</summary>
    public static IReadOnlyList<PadTarget> All { get; } =
    [
        A, B, One, Two, Plus, Minus, Home, Dpad, Recenter, NunchukStick, NunchukC, NunchukZ,
    ];
}
