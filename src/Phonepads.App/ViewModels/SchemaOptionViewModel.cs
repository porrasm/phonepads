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

    /// <summary>Reads out the controls in layout order, which is also importance order.</summary>
    public string Summary => string.Join(", ", Mapped.Schema.Controls.Select(Describe));

    private static string Describe(SchemaControl control) => control.Type switch
    {
        ControlType.Button => control.Label ?? control.Id,
        ControlType.Gyro => control.Id + " (tilt)",
        _ => control.Mode switch
        {
            ControlMode.Dpad => control.Id + " (dpad)",
            ControlMode.XOnly => control.Id + " (x axis)",
            ControlMode.YOnly => control.Id + " (y axis)",
            _ => control.Id + " (stick)",
        },
    };
}
