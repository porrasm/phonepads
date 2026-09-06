using System.Text;
using Phonepads.Core;

namespace Phonepads.Dsu;

/// <summary>
/// Generates Dolphin "Emulated Wii Remote" profiles that bind a Phonepads DSU slot, so nobody
/// has to hand-map twenty inputs. The Wii-button side comes from <see cref="WiiLayout"/> and
/// the DSU-button side from the same placement <see cref="DsuPackets.ButtonsFrom"/> uses, so
/// the profile cannot drift from what the wire carries.
/// </summary>
public static class DolphinProfile
{
    /// <summary>The description the user gives the server in Dolphin; part of every device name.</summary>
    public const string ServerDescription = "Phonepads";

    /// <summary>Dolphin's ini key for the sideways-remote option (its <c>SIDEWAYS_OPTION</c> constant).</summary>
    public const string SidewaysOptionKey = "Options/Sideways Wiimote";

    public enum Variant
    {
        Remote,
        RemoteWithNunchuk,
        RemoteSideways,
    }

    public static IReadOnlyList<Variant> Variants { get; } =
        [Variant.Remote, Variant.RemoteWithNunchuk, Variant.RemoteSideways];

    /// <summary>Dolphin's device name for a slot: the index is the DSU slot, the rest the server description.</summary>
    public static string DeviceName(int slot) => $"DSUClient/{slot}/{ServerDescription}";

    public static string FileName(Variant variant, int slot) =>
        $"Phonepads {Label(variant)} (P{slot + 1}).ini";

    public static string Label(Variant variant) => variant switch
    {
        Variant.RemoteWithNunchuk => "Wii Remote + Nunchuk",
        Variant.RemoteSideways => "Wii Remote Sideways",
        _ => "Wii Remote",
    };

    /// <summary>The DSU input Dolphin exposes for a pad target, as it appears in a profile expression.</summary>
    public static string DsuInputName(PadTarget target) => target switch
    {
        PadTarget.A => "Cross",
        PadTarget.B => "Circle",
        PadTarget.X => "Square",
        PadTarget.Y => "Triangle",
        PadTarget.LeftBumper => "L1",
        PadTarget.RightBumper => "R1",
        PadTarget.LeftTrigger => "L2",
        PadTarget.RightTrigger => "R2",
        PadTarget.Back => "Share",
        PadTarget.Start => "Options",
        PadTarget.Guide => "PS",
        PadTarget.LeftThumbClick => "L3",
        PadTarget.RightThumbClick => "R3",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Not a single DSU input."),
    };

    public static string Generate(Variant variant, int slot)
    {
        var ini = new StringBuilder();
        ini.AppendLine("[Profile]");
        ini.AppendLine($"Device = {DeviceName(slot)}");

        Bind(ini, "Buttons/A", WiiLayout.A);
        Bind(ini, "Buttons/B", WiiLayout.B);
        Bind(ini, "Buttons/1", WiiLayout.One);
        Bind(ini, "Buttons/2", WiiLayout.Two);
        Bind(ini, "Buttons/-", WiiLayout.Minus);
        Bind(ini, "Buttons/+", WiiLayout.Plus);
        Bind(ini, "Buttons/Home", WiiLayout.Home);

        ini.AppendLine("D-Pad/Up = `Pad N`");
        ini.AppendLine("D-Pad/Down = `Pad S`");
        ini.AppendLine("D-Pad/Left = `Pad W`");
        ini.AppendLine("D-Pad/Right = `Pad E`");

        // Motion Input: Dolphin auto-fills these for DSU devices, but explicit is portable.
        foreach (var axis in new[] { "Up", "Down", "Left", "Right", "Forward", "Backward" })
            ini.AppendLine($"IMUAccelerometer/{axis} = `Accel {axis}`");
        foreach (var axis in new[] { "Pitch Up", "Pitch Down", "Roll Left", "Roll Right", "Yaw Left", "Yaw Right" })
            ini.AppendLine($"IMUGyroscope/{axis} = `Gyro {axis}`");

        // Pointing from the gyro, no sensor bar needed. Yaw drifts; Recenter fixes it.
        ini.AppendLine("IMUIR/Enabled = True");
        ini.AppendLine("IMUIR/Total Yaw = 25");
        Bind(ini, "IMUIR/Recenter", WiiLayout.Recenter);

        // Never let button-driven motion simulation fight the real sensors.
        ini.AppendLine("Shake/X = ");
        ini.AppendLine("Shake/Y = ");
        ini.AppendLine("Shake/Z = ");

        switch (variant)
        {
            case Variant.RemoteWithNunchuk:
                ini.AppendLine("Extension = Nunchuk");
                Bind(ini, "Nunchuk/Buttons/C", WiiLayout.NunchukC);
                Bind(ini, "Nunchuk/Buttons/Z", WiiLayout.NunchukZ);
                // The wire's Y grows downwards (DualShock heritage) and Dolphin's DSU inputs
                // do not flip it, so "up" is the negative direction here.
                ini.AppendLine("Nunchuk/Stick/Up = `Left Y-`");
                ini.AppendLine("Nunchuk/Stick/Down = `Left Y+`");
                ini.AppendLine("Nunchuk/Stick/Left = `Left X-`");
                ini.AppendLine("Nunchuk/Stick/Right = `Left X+`");
                break;

            case Variant.RemoteSideways:
                ini.AppendLine("Extension = None");
                ini.AppendLine($"{SidewaysOptionKey} = True");
                break;

            default:
                ini.AppendLine("Extension = None");
                break;
        }

        return ini.ToString();
    }

    /// <summary>Writes every variant for every slot into <paramref name="directory"/>; returns the paths.</summary>
    public static IReadOnlyList<string> WriteAll(string directory)
    {
        Directory.CreateDirectory(directory);
        var written = new List<string>();

        foreach (var variant in Variants)
        {
            for (var slot = 0; slot < DsuServerCore.SlotCount; slot++)
            {
                var path = Path.Combine(directory, FileName(variant, slot));
                File.WriteAllText(path, Generate(variant, slot));
                written.Add(path);
            }
        }

        return written;
    }

    /// <summary>
    /// Dolphin's Wii Remote profile folders in the places a Windows install keeps its user
    /// data. Only existing folders are returned; nothing is created.
    /// </summary>
    public static IReadOnlyList<string> FindDolphinProfileDirectories()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Dolphin Emulator", "Config", "Profiles", "Wiimote"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Dolphin Emulator", "Config", "Profiles", "Wiimote"),
        };

        return candidates.Where(Directory.Exists).ToList();
    }

    private static void Bind(StringBuilder ini, string key, PadTarget target) =>
        ini.AppendLine($"{key} = `{DsuInputName(target)}`");
}
