# Mobile KBM

Your phone as this PC's mouse and keyboard. A tray app with no window: it keeps a session with the
[Gamepad service](https://gamepad.porras.club) open around the clock, and whatever you do on the
phone happens on the PC.

It is deliberately not a full keyboard. It covers what a phone is good at: pointing, scrolling,
typing a line of text, and the usual shortcuts. For anything unusual, the **Keyboard** button opens
Windows' own on-screen keyboard, which the phone's touchpad can then use.

## Setup

1. On the Gamepad website, log in and make a key under **Driver keys**. Copy it; it is shown once.
2. Start `mobile-kbm.exe`. On first run it asks for the key.
3. On your phone, open **gamepad.porras.club** and log in with the same account. The PC's session
   is listed there with no code to type; tap it. The phone remembers it and rejoins by itself after
   a drop.

The tray icon's menu shows where things stand and offers the only settings there are:

- **On your phone: gamepad.porras.club** — the hint above. Clicking it opens the site.
- **Pause / Resume** — while paused, the session stays up but nothing reaches the PC. Double-clicking
  the icon does the same.
- **Driver key…** — enter a new key. Any current session is replaced.
- **Quit** — ends the session, so the phone is told right away.

Icon colours: green is ready, grey is paused, amber is connecting or retrying, red means it needs a
key.

To start it with Windows, put a shortcut to `mobile-kbm.exe` in `shell:startup`.

## Layouts

Pick a layout on the phone from its menu. The layout is only a starting point: you can drag and
resize every control on the phone, and your arrangement is remembered across sessions.

| Layout | For | Controls |
|---|---|---|
| **Mouse** (default) | Everyday use | Touchpad, Left, Right, scroll stick, Type, Enter, ⌫, Esc, Keyboard |
| **Touchpad only** | Maximum pointing area | The whole screen is a touchpad |
| **Browser** | Browsing from the sofa | Touchpad, Left, scroll, Type, Enter, Back, Forward, Address (Ctrl+L), New tab, Close tab, Next tab, Reload, Esc, Right |
| **Keys** | Navigating without a pointer | Arrow pad, Enter, Esc, Tab, ⌫, Del, Space, Type, Alt+Tab, Start, Copy, Paste, Undo, Desktop (Win+D), Close window (Alt+F4), Keyboard |
| **Media** | A PC on the TV | Play/Pause, seek ◀▶, Vol −/+, Mute, Full screen (F), Prev/Next track, Esc, small touchpad, Click |
| **Slides** | Presenting | Next/Previous (Page Down/Up, like a clicker), Start show (F5), Black screen (B), End show (Esc) |

**Type** opens the phone's keyboard. The text is typed on the PC when you press Send, exactly as
written, whatever the PC's keyboard layout. It does not press Enter afterwards; use the Enter button.

### Touchpad gestures

| Gesture | Does |
|---|---|
| Slide one finger | Moves the pointer (faster when you move faster) |
| Double-tap in the same place | Left click. A single tap does nothing, so lifting and re-placing your thumb never clicks by accident. Two double-taps make a double-click. |
| Double-tap, keep the finger down and slide (or hold) | Drag |
| Two fingers slide | Scroll; the content follows your fingers, like on a phone |
| Two-finger tap | Right click |
| Three-finger tap | Middle click |

The buttons are real mouse buttons: hold **Left** and slide on the touchpad to drag. The scroll stick
scrolls faster the further you push it. Held keys such as ⌫, the arrows and Vol ± auto-repeat like
a real keyboard. The repeat stops after 6 seconds, so a phone that vanishes mid-press cannot delete a
whole document.

### Touchpad vs raw touch

The protocol offers two ways to read fingers: a laid-out **touchpad** control, and **raw**, which
turns the background behind the other controls into a touch surface. Both report absolute finger
positions, so the gestures are the same code either way. The difference is shape. A touchpad declares
its aspect ratio, so the pointer moves at the same speed across and down. Raw reports 0–1 of a box
whose proportions the app is never told. So layouts that mix controls use a touchpad. The
touchpad-only layout uses raw to get the whole screen, and assumes a typical portrait phone's
proportions (`KbmSchemas.PortraitBoxAspect`), which is at worst a little off on one axis.

## How it stays connected

The session is always **private**: no join code exists, and only the key's owner (and emails linked
to the key on the website) can join. This is a remote control for a PC. It is also always
**lobbyless**: it starts at once and stays open to phones coming and going.

`SessionKeeper` loops forever:

- It creates a session with the driver key, replacing whatever that key held before (an earlier run,
  or a crash).
- It starts the session as soon as it sees it, and resumes it if the service paused it while the
  connection was down.
- It reconnects to the same session after a drop, pinging every 25 s so idle connections survive
  proxies.
- When the session is gone for good, it makes a new one. That covers: ended from the website, closed
  because the app was away for 3 minutes, idle for 24 hours, or a dead token. If reconnecting has
  failed for 2½ minutes, it stops trying the old session, which the service is about to close anyway.
- Failures back off from 1 s up to 60 s. Sessions that die straight away back off too, so two PCs
  sharing one key do not fight at full speed. Use one key per PC.
- It only stops and waits when there is no key, or the service refuses it. The menu then says so.

Anything held (keys, buttons, a drag) is let go on pause, on a dropped phone, on a dropped connection
and on a lost session.

## Limits

- Windows does not let a normal app send input to a window running as administrator (UIPI), to the
  lock screen or to UAC prompts. Run Mobile KBM as administrator if you need the former.
- Latency is phone → service → PC. That is fine for desktop use, but this is not a gaming mouse.
- The driver key is stored in `mobile-kbm.settings.json` beside the exe, encrypted with DPAPI for
  the current Windows user. Copying the folder to another PC or user means entering the key again.

## Building

```bash
dotnet build mobile-kbm/MobileKbm.slnx
dotnet test mobile-kbm/MobileKbm.slnx
dotnet publish mobile-kbm/src/MobileKbm.App -c Release -p:DebugType=none -o publish/mobile-kbm
```

The publish step produces a single self-contained `mobile-kbm.exe`. WinForms cannot be trimmed, so it
is larger than Phonepads. Adding `--self-contained false` gives a small exe that needs the .NET 10
Desktop Runtime installed.

## Layout

| Project | What lives there |
|---|---|
| `src/MobileKbm.Core` | Layouts, touchpad gestures, key state, the session keeper. No Windows APIs, so it is fully testable. |
| `src/MobileKbm.App` | The tray icon, the key dialog, `SendInput`, DPAPI settings (WinForms) |
| `tests/MobileKbm.Tests` | Core: layouts, gestures, keys, the keeper against a fake service |
