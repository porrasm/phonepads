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
/// screen up — that makes +x right, +y forward and +z up. Each gyro field is the phone axis
/// that rotates the remote about the matching sensor axis: pitch about the right axis (+x),
/// roll about the forward axis (+y), yaw about the up axis (+z).
///
/// The signs are the DualShock 4's, which cemuhook inherits — and the DS4 does <i>not</i>
/// follow the naive right-hand rule on every axis, so they are settled by testing against
/// Dolphin's live Motion Input bars, not by derivation:
/// <list type="bullet">
///   <item>Pitch is inverted from the right-hand rule: nose-up is −pitch on a DS4, so we send
///     <c>−gx</c>. (Sending <c>+gx</c> pointed the cursor up when you aimed down.)</item>
///   <item>Yaw is negated: a right-hand turn about the up axis swings the nose left, and
///     Dolphin's <c>Yaw Right</c> wants right-positive.</item>
///   <item>Roll is passed straight through; tipping the top edge right reads as roll-right.</item>
/// </list>
/// Accelerometer signs match Dolphin's DSU inputs (<c>Accel Up = −accel_y</c>,
/// <c>Accel Left = +accel_x</c>, <c>Accel Forward = +accel_z</c>).
/// </summary>
public static class WiiMotionFrame
{
    public static DsuMotion FromPhone(in MotionOutput phone) => new(
        AccelX: (float)-phone.AccelX,
        AccelY: (float)-phone.AccelZ,
        AccelZ: (float)phone.AccelY,
        GyroPitch: (float)-phone.RateX,
        GyroYaw: (float)-phone.RateZ,
        GyroRoll: (float)phone.RateY);
}
