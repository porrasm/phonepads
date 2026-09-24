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
- **Settings…** — the driver key (a new one replaces any current session; leave it empty to keep
  the current one), **Pointer speed** (a slider from 0.25× to 4× for the touchpad), and **Start
  with Windows** (off by default).
- **Quit** — ends the session, so the phone is told right away.

Icon colours: green is ready, grey is paused, amber is connecting or retrying, red means it needs a
key.

**Start with Windows** adds Mobile KBM to your user's startup programs (no admin needed); it then
appears in Task Manager's Startup tab, and disabling it there is respected. The app is portable:
if you move its folder, the entry follows the next time you run it from the new place.

## Layouts

Pick a layout on the phone from its menu. The layout is only a starting point: you can drag and
resize every control on the phone, and your arrangement is remembered across sessions.

| Layout | For | Controls |
|---|---|---|
| **Mouse** (default) | Everyday use | The whole background is the touchpad; Left and Right along the bottom, a scroll strip down the right edge, and Type, Enter, ⌫, Esc, All keys along the top |
| **Touchpad only** | Maximum pointing area | The whole screen is a touchpad |
| **Browser** | Browsing from the sofa | Like Mouse, with Back, Fwd, Next tab, Close tab, URL (Ctrl+L), Type and Enter along the top |
| **Keys** | Navigating without a pointer | A big arrow pad, Enter, Esc, ⌫, Del, Tab, Space, Type, All keys |
| **Shortcuts** | The everyday Windows shortcuts | Copy, Paste, Cut, Undo, Redo, Select all, Alt+Tab, Start, Desktop (Win+D), Close window (Alt+F4), Save, All keys |
| **Media** | A PC on the TV | Prev, Play/Pause, Next, seek « », Full screen (F), Vol −/+, Mute, Esc, Click; the background is a touchpad |
| **Slides** | Presenting | Next/Previous (Page Down/Up, like a clicker), Start show (F5), Black screen (B), End show (Esc) |

**Type** opens the phone's keyboard. The text is typed on the PC when you press Send, exactly as
written, whatever the PC's keyboard layout. It does not press Enter afterwards; use the Enter button.
**All keys** opens Windows' own on-screen keyboard, for anything the layouts leave out.

Each layout places its controls itself rather than leaving it to the phone. The phone's automatic
layout fills from the right thumb outwards (which put Left on the right) and never makes a pointer
area bigger than one box. One thing only the phone decides is button size: it shrinks every button
as their number grows, and asking for "large" does not override that. That is why Keys and Shortcuts
are separate layouts, and why Browser keeps to seven keys.

### Touchpad gestures

| Gesture | Does |
|---|---|
| Slide one finger | Moves the pointer (faster when you move faster) |
| Double-tap in the same place | Left click. A single tap does nothing, so lifting and re-placing your thumb never clicks by accident. Two double-taps make a double-click. |
| Double-tap, keep the finger down and slide (or hold) | Drag |
| Two fingers slide | Scroll; the content follows your fingers, like on a phone |
| Two-finger tap | Right click |
| Three-finger tap | Middle click |

The buttons are real mouse buttons: hold **Left** and slide on the touchpad to drag. The **scroll
strip** scrolls as you drag a finger along it, the content following your finger; a full-length drag
is about ten notches of the wheel. Held keys such as ⌫, the arrows and Vol ± auto-repeat like a real
keyboard. The repeat stops after 6 seconds, so a phone that vanishes mid-press cannot delete a whole
document.

### Touchpad vs raw touch

The protocol offers two ways to read fingers: a laid-out **touchpad** control, and **raw**, which
turns the background behind the other controls into a touch surface. Both report absolute finger
positions, so the gestures are the same code either way. A finger that lands on a button belongs to
the button, so raw works as a touchpad around and between the other controls.

The difference is shape. A touchpad declares its aspect ratio, so its motion is the same speed
across and down. Raw reports 0–1 of a box whose proportions the app is never told, so it assumes a
typical portrait phone's (`KbmSchemas.PortraitBoxAspect`), which is at worst a little off on one axis.
The layouts use raw anyway: a pointer area as big as the screen is worth far more. The scroll strip
is a touchpad, the narrowest the protocol allows (aspect 0.25).

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
