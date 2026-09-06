using Phonepads.Protocol;

namespace Phonepads.Core;

/// <summary>
/// One emulated controller. The interface exists so backends can be swapped — ViGEmBus for
/// its successor, DSU for another motion protocol — without the rest of the app noticing.
/// </summary>
public interface IVirtualPad : IDisposable
{
    /// <summary>Zero-based pad slot, 0-3. Windows accepts at most four XInput controllers.</summary>
    int Slot { get; }

    PadBackend Backend { get; }

    void Update(PadState state);

    /// <summary>
    /// Feeds raw inertial samples, oldest first. Backends without a motion channel ignore them.
    /// </summary>
    void PushMotion(ReadOnlySpan<MotionSample> samples);

    /// <summary>Releases every input, so nothing is left held down.</summary>
    void Reset();

    /// <summary>Force feedback from the game: large (low-frequency) and small motor strengths.</summary>
    event Action<byte, byte>? RumbleChanged;
}

/// <summary>Creates virtual pads of one backend, when the machine can host them at all.</summary>
public interface IVirtualPadHub : IDisposable
{
    PadBackend Backend { get; }

    /// <summary>False when the driver or port is missing; the app stays usable for everything else (SETUP-2).</summary>
    bool IsAvailable { get; }

    /// <summary>A user-facing explanation when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableReason { get; }

    IVirtualPad Create(int slot);
}

/// <summary>
/// Stand-in hub used when a backend is absent. Everything upstream keeps working, so
/// schemas and mappings can still be edited on a machine with nothing installed.
/// </summary>
public sealed class NullVirtualPadHub(PadBackend backend, string reason) : IVirtualPadHub
{
    public PadBackend Backend { get; } = backend;

    public bool IsAvailable => false;

    public string? UnavailableReason { get; } = reason;

    public IVirtualPad Create(int slot) => new NullPad(slot, Backend);

    public void Dispose()
    {
    }

    private sealed class NullPad(int slot, PadBackend backend) : IVirtualPad
    {
        public int Slot => slot;

        public PadBackend Backend => backend;

        public event Action<byte, byte>? RumbleChanged
        {
            add { }
            remove { }
        }

        public void Update(PadState state)
        {
        }

        public void PushMotion(ReadOnlySpan<MotionSample> samples)
        {
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}
