using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Phonepads.Core;

namespace Phonepads.App.ViewModels;

/// <summary>A preset offered on the setup screen, with a one-line summary of its controls.</summary>
public partial class SchemaOptionViewModel(MappedSchema mapped) : ViewModelBase
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public MappedSchema Mapped { get; } = mapped;

    public string Name => Mapped.Schema.Name;

    public bool RequiresGyro => Mapped.Schema.RequiresGyro;

    public PadBackend Backend => Mapped.Mapping.Backend;

    public bool IsWiiRemote => Backend == PadBackend.WiiRemote;

    /// <summary>A real controller paired with the phone rather than an on-screen layout.</summary>
    public bool IsPhysicalGamepad => Mapped.Schema.IsPhysicalGamepad;

    /// <summary>What the game will see: an Xbox pad works everywhere, a Wii Remote only in Dolphin.</summary>
    public string BackendLabel => IsWiiRemote ? "Wii Remote · Dolphin" : "Xbox controller";

    /// <summary>Reads out the controls in layout order, which is also importance order.</summary>
    public string Summary => IsPhysicalGamepad
        ? "A controller paired with the phone over Bluetooth or USB — sticks, dpad, ABXY, bumpers, " +
          "analog triggers and the rest, passed straight through to an Xbox pad."
        : string.Join(", ", Mapped.Schema.Controls.Select(Describe));

    private static string Describe(SchemaControl control) => control.Type switch
    {
        ControlType.Button => control.Label ?? control.Id,
        ControlType.Trigger => (control.Label ?? control.Id) + " (trigger)",
        ControlType.Gyro => control.Id + " (tilt)",
        ControlType.Motion => "motion sensors",
        _ => control.Mode switch
        {
            ControlMode.Dpad => control.Id + " (dpad)",
            ControlMode.XOnly => control.Id + " (x axis)",
            ControlMode.YOnly => control.Id + " (y axis)",
            ControlMode.Relative => control.Id + " (relative pad)",
            _ => control.Id + " (stick)",
        },
    };
}
