using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Phonepads.Core;

namespace Phonepads.App.ViewModels;

/// <summary>One row of the lobby / player list, including what its pad is currently doing.</summary>
public partial class PlayerRowViewModel(SessionPlayer player) : ViewModelBase
{
    [ObservableProperty]
    public partial string Name { get; set; } = player.Info.Name;

    [ObservableProperty]
    public partial string Color { get; set; } = player.Info.Color;

    [ObservableProperty]
    public partial bool Ready { get; set; } = player.Info.Ready;

    [ObservableProperty]
    public partial bool Connected { get; set; } = player.Info.Connected;

    [ObservableProperty]
    public partial string SchemaName { get; set; } = player.Info.SchemaId ?? "—";

    [ObservableProperty]
    public partial string SlotLabel { get; set; } =
        player.HasPad ? $"Pad {player.Slot + 1}" : "No pad";

    /// <summary>Live pad state, so a mapping can be checked without alt-tabbing into a game.</summary>
    [ObservableProperty]
    public partial string PadState { get; set; } = "idle";

    public string Id { get; } = player.Info.Id;

    public string StatusLine => Connected ? (Ready ? "ready" : "not ready") : "disconnected";

    /// <summary>The player's chosen colour, so rows are as identifiable here as on the phone.</summary>
    public IBrush Swatch
    {
        get
        {
            try
            {
                return new SolidColorBrush(Avalonia.Media.Color.Parse(Color));
            }
            catch (FormatException)
            {
                return Brushes.Gray;
            }
        }
    }

    partial void OnColorChanged(string value) => OnPropertyChanged(nameof(Swatch));

    public void Update(SessionPlayer player, string schemaName)
    {
        Name = player.Info.Name;
        Color = player.Info.Color;
        Ready = player.Info.Ready;
        Connected = player.Info.Connected;
        SchemaName = schemaName;
        SlotLabel = player.HasPad ? $"Pad {player.Slot + 1}" : "No pad";
        OnPropertyChanged(nameof(StatusLine));
    }

    partial void OnReadyChanged(bool value) => OnPropertyChanged(nameof(StatusLine));

    partial void OnConnectedChanged(bool value) => OnPropertyChanged(nameof(StatusLine));

    /// <summary>Renders a pad state compactly enough to read at a glance while playing.</summary>
    public void ShowPad(PadState state)
    {
        var buttons = state.Buttons == PadButtons.None ? "-" : state.Buttons.ToString().Replace(", ", "+");
        PadState =
            $"L({Axis(state.LeftStickX)},{Axis(state.LeftStickY)}) " +
            $"R({Axis(state.RightStickX)},{Axis(state.RightStickY)}) " +
            $"LT {state.LeftTrigger} RT {state.RightTrigger}  {buttons}";
    }

    private static string Axis(short value) => (value / 32767d).ToString("+0.00;-0.00; 0.00");
}
