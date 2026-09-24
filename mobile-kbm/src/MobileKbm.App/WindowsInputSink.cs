using System.Runtime.InteropServices;
using MobileKbm.Core;

namespace MobileKbm.App;

/// <summary>
/// Keyboard and mouse through SendInput — injected exactly like a USB keyboard and mouse,
/// into whatever window has focus. Windows keeps one limit: input cannot reach a window
/// running as administrator unless this app does too (UIPI), nor the lock or UAC screens.
/// </summary>
internal sealed class WindowsInputSink : IInputSink
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseHWheel = 0x1000;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private const uint KeyExtended = 0x0001;
    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;

    private const uint MapVkToVsc = 0;

    public int ScreenHeight => Screen.PrimaryScreen?.Bounds.Height ?? 1080;

    /// <summary>
    /// Moves the pointer by exactly this many pixels. A relative SendInput move would go
    /// through Windows' pointer speed and "Enhance pointer precision" curve on top of the
    /// touchpad's own acceleration, so the pointer is placed absolutely instead.
    /// </summary>
    public void MoveMouse(int dx, int dy)
    {
        if (!GetCursorPos(out var at))
        {
            // No cursor to read (a secure desktop): a relative move is the best there is.
            Send(Mouse(dx, dy, 0, MouseMove));
            return;
        }

        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = Math.Max(GetSystemMetrics(SmCxVirtualScreen), 2);
        var height = Math.Max(GetSystemMetrics(SmCyVirtualScreen), 2);

        var x = Math.Clamp(at.X + dx, left, left + width - 1);
        var y = Math.Clamp(at.Y + dy, top, top + height - 1);
        Send(Mouse(Normalize(x - left, width), Normalize(y - top, height), 0, MouseMove | MouseAbsolute | MouseVirtualDesk));
    }

    /// <summary>Pixel offset to the 0–65535 range absolute input uses, landing on that pixel's centre.</summary>
    private static int Normalize(int offset, int size) => (int)(((offset * 65536L) + 32768) / size);

    public void SetMouseButton(MouseButton button, bool down)
    {
        var flags = button switch
        {
            MouseButton.Left => down ? MouseLeftDown : MouseLeftUp,
            MouseButton.Right => down ? MouseRightDown : MouseRightUp,
            _ => down ? MouseMiddleDown : MouseMiddleUp,
        };
        Send(Mouse(0, 0, 0, flags));
    }

    public void Wheel(int delta, bool horizontal) =>
        Send(Mouse(0, 0, unchecked((uint)delta), horizontal ? MouseHWheel : MouseWheel));

    public void SetKey(Key key, bool down)
    {
        var vk = (ushort)key;
        var flags = (IsExtended(key) ? KeyExtended : 0) | (down ? 0 : KeyUp);
        Send(Keyboard(vk, (ushort)MapVirtualKey(vk, MapVkToVsc), flags));
    }

    public void TypeText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var c in text)
        {
            switch (c)
            {
                // Line breaks and tabs are keys, not characters, to the apps receiving them.
                case '\r':
                    continue;
                case '\n':
                    AddTap(inputs, Key.Enter);
                    continue;
                case '\t':
                    AddTap(inputs, Key.Tab);
                    continue;
            }

            // Unicode input types the character itself, whatever the keyboard layout. Characters
            // outside the BMP arrive as two UTF-16 units, which is exactly what Windows expects.
            inputs.Add(Keyboard(0, c, KeyUnicode));
            inputs.Add(Keyboard(0, c, KeyUnicode | KeyUp));
        }

        // In moderate batches: some apps drop keys from one enormous burst.
        const int Batch = 64;
        for (var i = 0; i < inputs.Count; i += Batch)
            Send(inputs.Skip(i).Take(Batch).ToArray());
    }

    private static void AddTap(List<INPUT> inputs, Key key)
    {
        var vk = (ushort)key;
        var scan = (ushort)MapVirtualKey(vk, MapVkToVsc);
        inputs.Add(Keyboard(vk, scan, 0));
        inputs.Add(Keyboard(vk, scan, KeyUp));
    }

    /// <summary>
    /// Keys that live on the extended part of the keyboard. Without the flag Windows reads an
    /// arrow as its numpad twin (with Num Lock on, a digit).
    /// </summary>
    private static bool IsExtended(Key key) => key is
        Key.PageUp or Key.PageDown or Key.End or Key.Home
        or Key.Left or Key.Up or Key.Right or Key.Down or Key.Delete
        or Key.Win
        or Key.BrowserBack or Key.BrowserForward or Key.BrowserRefresh
        or Key.VolumeMute or Key.VolumeDown or Key.VolumeUp
        or Key.MediaNext or Key.MediaPrevious or Key.MediaPlayPause;

    private static INPUT Mouse(int dx, int dy, uint data, uint flags) => new()
    {
        type = InputMouse,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } },
    };

    private static INPUT Keyboard(ushort vk, ushort scan, uint flags) => new()
    {
        type = InputKeyboard,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    private static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        // A zero return means something blocked it (the secure desktop, UIPI). Nothing to do about
        // that from here, and a remote that throws on a locked screen would be worse.
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    // Field names follow the Win32 headers.
#pragma warning disable IDE1006
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // The union must include MOUSEINPUT, the largest member, or cbSize is wrong and SendInput fails.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
#pragma warning restore IDE1006
}
