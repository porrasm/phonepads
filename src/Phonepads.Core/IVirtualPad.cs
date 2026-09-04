namespace Phonepads.Core;

/// <summary>
/// One emulated controller. The interface exists so the ViGEmBus backend can be swapped for
/// its successor, or a future non-Windows backend, without the rest of the app noticing.
/// </summary>
public interface IVirtualPad : IDisposable
{
    /// <summary>Zero-based pad slot, 0-3. Windows accepts at most four XInput controllers.</summary>
    int Slot { get; }

    void Update(PadState state);

    /// <summary>Releases every input, so nothing is left held down.</summary>
    void Reset();

    /// <summary>Force feedback from the game: large (low-frequency) and small motor strengths.</summary>
    event Action<byte, byte>? RumbleChanged;
}

/// <summary>Creates virtual pads, when the machine can host them at all.</summary>
public interface IVirtualPadHub : IDisposable
{
    /// <summary>False when the driver is missing; the app stays usable for everything else (SETUP-2).</summary>
    bool IsAvailable { get; }

    /// <summary>A user-facing explanation when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableReason { get; }

    IVirtualPad Create(int slot);
}

/// <summary>
/// Stand-in hub used when the driver is absent. Everything upstream keeps working, so
/// schemas and mappings can still be edited on a machine with no ViGEmBus installed.
/// </summary>
public sealed class NullVirtualPadHub(string reason) : IVirtualPadHub
{
    public bool IsAvailable => false;

    public string? UnavailableReason { get; } = reason;

    public IVirtualPad Create(int slot) => new NullPad(slot);

    public void Dispose()
    {
    }

    private sealed class NullPad(int slot) : IVirtualPad
    {
        public int Slot => slot;

        public event Action<byte, byte>? RumbleChanged
        {
            add { }
            remove { }
        }

        public void Update(PadState state)
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
