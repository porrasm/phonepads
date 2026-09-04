using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Phonepads.Core;

namespace Phonepads.VirtualPads;

/// <summary>
/// Virtual Xbox 360 pads backed by ViGEmBus. The driver is a signed kernel driver the user
/// installs once per machine — the one part of the app that is not portable (SETUP-2).
/// </summary>
public sealed class ViGEmPadHub : IVirtualPadHub
{
    public const string DownloadUrl = "https://github.com/nefarius/ViGEmBus/releases";

    private readonly ViGEmClient? _client;

    private ViGEmPadHub(ViGEmClient? client, string? unavailableReason)
    {
        _client = client;
        UnavailableReason = unavailableReason;
    }

    public bool IsAvailable => _client is not null;

    public string? UnavailableReason { get; }

    /// <summary>
    /// Connects to the driver if it is installed. Never throws: a missing driver leaves the
    /// app fully usable for editing schemas and mappings, just unable to create pads.
    /// </summary>
    public static ViGEmPadHub Detect()
    {
        try
        {
            return new ViGEmPadHub(new ViGEmClient(), null);
        }
        catch (Exception ex)
        {
            return new ViGEmPadHub(null,
                "The ViGEmBus controller driver is not installed, so Phonepads cannot create "
                + "controllers yet. Install it once from " + DownloadUrl + " and restart the app. "
                + "(" + ex.Message + ")");
        }
    }

    public IVirtualPad Create(int slot)
    {
        if (_client is null)
            throw new InvalidOperationException("The ViGEmBus driver is not available.");

        return new ViGEmPad(_client, slot);
    }

    public void Dispose() => _client?.Dispose();

    private sealed class ViGEmPad : IVirtualPad
    {
        private readonly IXbox360Controller _controller;
        private bool _connected;

        public ViGEmPad(ViGEmClient client, int slot)
        {
            Slot = slot;
            _controller = client.CreateXbox360Controller();

            // One report per input frame rather than one per axis write.
            _controller.AutoSubmitReport = false;
            _controller.FeedbackReceived += OnFeedback;
            _controller.Connect();
            _connected = true;
        }

        public int Slot { get; }

        public event Action<byte, byte>? RumbleChanged;

        public void Update(PadState state)
        {
            if (!_connected) return;

            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, state.LeftStickX);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, state.LeftStickY);
            _controller.SetAxisValue(Xbox360Axis.RightThumbX, state.RightStickX);
            _controller.SetAxisValue(Xbox360Axis.RightThumbY, state.RightStickY);
            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
            _controller.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);

            _controller.SetButtonState(Xbox360Button.A, state.IsPressed(PadButtons.A));
            _controller.SetButtonState(Xbox360Button.B, state.IsPressed(PadButtons.B));
            _controller.SetButtonState(Xbox360Button.X, state.IsPressed(PadButtons.X));
            _controller.SetButtonState(Xbox360Button.Y, state.IsPressed(PadButtons.Y));
            _controller.SetButtonState(Xbox360Button.LeftShoulder, state.IsPressed(PadButtons.LeftBumper));
            _controller.SetButtonState(Xbox360Button.RightShoulder, state.IsPressed(PadButtons.RightBumper));
            _controller.SetButtonState(Xbox360Button.Back, state.IsPressed(PadButtons.Back));
            _controller.SetButtonState(Xbox360Button.Start, state.IsPressed(PadButtons.Start));
            _controller.SetButtonState(Xbox360Button.LeftThumb, state.IsPressed(PadButtons.LeftThumb));
            _controller.SetButtonState(Xbox360Button.RightThumb, state.IsPressed(PadButtons.RightThumb));
            _controller.SetButtonState(Xbox360Button.Up, state.IsPressed(PadButtons.DpadUp));
            _controller.SetButtonState(Xbox360Button.Down, state.IsPressed(PadButtons.DpadDown));
            _controller.SetButtonState(Xbox360Button.Left, state.IsPressed(PadButtons.DpadLeft));
            _controller.SetButtonState(Xbox360Button.Right, state.IsPressed(PadButtons.DpadRight));

            _controller.SubmitReport();
        }

        public void Reset() => Update(PadState.Neutral);

        private void OnFeedback(object sender, Xbox360FeedbackReceivedEventArgs e) =>
            RumbleChanged?.Invoke(e.LargeMotor, e.SmallMotor);

        public void Dispose()
        {
            if (!_connected) return;
            _connected = false;

            _controller.FeedbackReceived -= OnFeedback;
            try
            {
                _controller.Disconnect();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The pad may already be gone if the driver was stopped underneath us.
            }
        }
    }
}
