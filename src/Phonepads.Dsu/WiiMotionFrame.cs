using Phonepads.Core;

namespace Phonepads.Dsu;

/// <summary>Inertial values in the DSU packet's own field order and sign conventions.</summary>
public readonly record struct DsuMotion(
    float AccelX,
    float AccelY,
    float AccelZ,
    float GyroPitch,
    float GyroYaw,
    float GyroRoll);

/// <summary>
/// Re-expresses the phone's motion as the DSU fields Dolphin expects for a Wii Remote.
///
/// The phone reports in its device frame: +x to the right of the screen, +y toward the top
/// edge, +z out of the screen. Held like a Wii Remote — upright, top edge toward the TV,
/// screen up — that makes +x right, +y forward and +z up. Dolphin's DSU client defines its
/// inputs with fixed signs (<c>Accel Up = −accel_y</c>, <c>Accel Left = +accel_x</c>,
/// <c>Accel Forward = +accel_z</c>, <c>Pitch Up = +pitch</c>, <c>Roll Right = +roll</c>,
/// <c>Yaw Right = +yaw</c>), so each field below is whichever phone axis produces the right
/// physical meaning under those signs. Worked through with the right-hand rule:
/// rotating about the right axis lifts the nose (pitch up), rotating about the forward axis
/// tips the top to the right (roll right), and rotating about the up axis turns the nose
/// <i>left</i>, hence the negated yaw.
/// </summary>
public static class WiiMotionFrame
{
    public static DsuMotion FromPhone(in MotionOutput phone) => new(
        AccelX: (float)-phone.AccelX,
        AccelY: (float)-phone.AccelZ,
        AccelZ: (float)phone.AccelY,
        GyroPitch: (float)phone.RateX,
        GyroYaw: (float)-phone.RateZ,
        GyroRoll: (float)phone.RateY);
}
