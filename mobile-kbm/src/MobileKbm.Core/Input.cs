namespace MobileKbm.Core;

/// <summary>
/// Keys the remote can press. The values are Windows virtual-key codes, so the Windows sink
/// passes them straight through; another platform's sink would translate them.
/// </summary>
public enum Key : ushort
{
    Backspace = 0x08,
    Tab = 0x09,
    Enter = 0x0D,
    Shift = 0x10,
    Ctrl = 0x11,
    Alt = 0x12,
    Escape = 0x1B,
    Space = 0x20,
    PageUp = 0x21,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Delete = 0x2E,

    A = 0x41, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    Win = 0x5B,

    F4 = 0x73,
    F5 = 0x74,
    F11 = 0x7A,

    BrowserBack = 0xA6,
    BrowserForward = 0xA7,
    BrowserRefresh = 0xA8,
    VolumeMute = 0xAD,
    VolumeDown = 0xAE,
    VolumeUp = 0xAF,
    MediaNext = 0xB0,
    MediaPrevious = 0xB1,
    MediaPlayPause = 0xB3,
}

public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>
/// The operating system's keyboard and mouse, as far as the remote needs them. Calls come
/// from one thread at a time (the controller serialises them), but not always the same one.
/// </summary>
public interface IInputSink
{
    /// <summary>Height of the primary screen in pixels; touchpad motion is scaled to it.</summary>
    int ScreenHeight { get; }

    /// <summary>Moves the pointer relative to where it is.</summary>
    void MoveMouse(int dx, int dy);

    void SetMouseButton(MouseButton button, bool down);

    /// <summary>
    /// Turns the wheel. 120 is one notch; positive scrolls up (vertical) or right (horizontal).
    /// </summary>
    void Wheel(int delta, bool horizontal);

    /// <summary>A key going down (again, for auto-repeat) or up.</summary>
    void SetKey(Key key, bool down);

    /// <summary>Types text as-is into whatever has focus, whatever the keyboard layout.</summary>
    void TypeText(string text);
}

/// <summary>
/// What the PC currently has held down, counted per holder. Two phone buttons that both use
/// Ctrl, or a touchpad drag and the Left button, must not release each other's input.
/// </summary>
internal sealed class HeldInput(IInputSink sink)
{
    private readonly Dictionary<Key, int> _keys = [];
    private readonly Dictionary<MouseButton, int> _buttons = [];

    public IInputSink Sink { get; } = sink;

    /// <summary>Pointer motion multiplier, set by the controller from the user's settings.</summary>
    public double PointerSpeed { get; set; } = 1;

    public void Press(Key key)
    {
        var count = _keys.GetValueOrDefault(key);
        _keys[key] = count + 1;
        if (count == 0) Sink.SetKey(key, down: true);
    }

    public void Release(Key key)
    {
        if (!_keys.TryGetValue(key, out var count)) return;
        if (count > 1)
        {
            _keys[key] = count - 1;
            return;
        }

        _keys.Remove(key);
        Sink.SetKey(key, down: false);
    }

    /// <summary>Another key-down for a held key — auto-repeat, which injected input does not get for free.</summary>
    public void Repeat(Key key)
    {
        if (_keys.ContainsKey(key)) Sink.SetKey(key, down: true);
    }

    public void Press(MouseButton button)
    {
        var count = _buttons.GetValueOrDefault(button);
        _buttons[button] = count + 1;
        if (count == 0) Sink.SetMouseButton(button, down: true);
    }

    public void Release(MouseButton button)
    {
        if (!_buttons.TryGetValue(button, out var count)) return;
        if (count > 1)
        {
            _buttons[button] = count - 1;
            return;
        }

        _buttons.Remove(button);
        Sink.SetMouseButton(button, down: false);
    }

    /// <summary>A full click, unless something is already holding that button down.</summary>
    public void Click(MouseButton button)
    {
        if (_buttons.ContainsKey(button)) return;
        Sink.SetMouseButton(button, down: true);
        Sink.SetMouseButton(button, down: false);
    }

    /// <summary>Lets go of everything — the safety net for pauses, drops and a lost session.</summary>
    public void ReleaseAll()
    {
        foreach (var key in _keys.Keys) Sink.SetKey(key, down: false);
        foreach (var button in _buttons.Keys) Sink.SetMouseButton(button, down: false);
        _keys.Clear();
        _buttons.Clear();
    }
}
