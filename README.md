# Phonepads

A portable Windows app that turns phones into real game controllers — Xbox pads for anything on
Windows, or Wii Remotes with motion for Dolphin.

It is a **driver** for the [Gamepad service](https://gamepad.porras.club): it claims a session with a
setup code, shows a join code for players to scan, receives their input over a WebSocket, and turns
that input into virtual controllers.

```
Phone → [Gamepad service] → Phonepads → Mapping → Virtual pad → Game
                                                  ├─ Xbox 360 pad (ViGEmBus)   → any Windows game
                                                  └─ Wii Remote  (DSU / UDP)    → Dolphin
```

See [specs.md](specs.md) for the user stories and [protocol.txt](protocol.txt) for the service protocol.

## Status

Early MVP. The vertical slice works end to end: claim a session, watch the lobby, start the game,
and drive virtual pads from phone input.

Working:

- Claiming a session with a setup code, with friendly errors for the failure cases
- Join code and scannable QR code
- Lobby with live player list, colours, ready state and pad assignment
- Start / pause / resume / end, with pads released to neutral on pause and removed on end
- **Two kinds of controller, mixable in one session**: each layout declares whether it drives an
  Xbox pad or a Wii Remote, and every player gets whichever their chosen layout needs
- Ten bundled layouts with default mappings: Generic Gamepad, a single-stick GameCube layout for
  Dolphin, Wii Remote (upright, with Nunchuk, and sideways), and five genre layouts
- Wii Remote mode streams the phone's raw motion sensors to Dolphin as an emulated Wii Remote with
  MotionPlus — tilts, shakes, swings and gyro pointing, no sensor bar needed
- Generated Dolphin controller profiles, so nobody hand-maps twenty inputs
- Live per-player pad and motion readout, so a mapping can be checked without a game running
- Automatic reconnection with backoff; terminal close codes are reported instead of retried
- Portable settings stored beside the executable

Not built yet: the schema editor, the mapping editor, schema import/export, and recreating a
session from a roster after the service closes it.

## Requirements

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) for Xbox controllers
- [Dolphin](https://dolphin-emu.org/) 5.0-11083 or newer for Wii Remote mode

ViGEmBus is a signed kernel driver, installed once per machine. It is the one part of the stack that
is not portable. Without it the app still runs: Xbox layouts cannot create pads, and it says so, but
Wii Remote layouts are unaffected — they never touch the driver.

## Building

```bash
dotnet build
```

```bash
dotnet test
```

## Running

```bash
dotnet run --project src/Phonepads.App
```

1. Create a session on the Gamepad website and copy the 6-character setup code.
2. Pick the layouts you want to offer, paste the code, and claim the session.
3. Players scan the QR code or type the join code on their phones.
4. Press **Start game** once everyone is ready.

## Wii Remote mode (Dolphin)

Dolphin cannot take motion from an Xbox pad — XInput has no motion channel. What it can take is the
**DSU protocol** (also called cemuhook): a small UDP server that carries buttons, sticks,
accelerometer and gyroscope for up to four controllers. Phonepads runs one on `127.0.0.1:26760`
whenever it starts. Dolphin re-synthesises a real Wii Remote's accelerometer report and MotionPlus
data from that feed, so games see the genuine article.

One-time Dolphin setup:

1. **Controllers → Alternate Input Sources**: enable the DSU client, add `127.0.0.1:26760`, and give it
   the description `Phonepads`. That name becomes part of every device name (`DSUClient/0/Phonepads`
   is player 1, `/1/` is player 2, and so on).
2. In Phonepads, open **Wii Remote mode** and press **Write Dolphin profiles**. They land in
   `dolphin/Wiimote/` beside the executable and, if Dolphin's user folder is in the usual place,
   straight into its `Config/Profiles/Wiimote/` as well.
3. For each Wii Remote in Dolphin: set it to **Emulated Wii Remote**, open Configure, and load the
   matching profile — *Phonepads Wii Remote (P1)* for player 1, and so on. There are three variants:
   plain remote, remote with Nunchuk, and sideways.

How to hold the phone: **like a remote** — upright, top edge toward the TV, screen up. The motion
feed is in the phone's own frame, so the phone simply *is* the remote's body. The sideways layout is
landscape for the same reason: a sideways phone is a sideways remote.

Pointing comes from the gyro with no sensor bar, so it drifts over a minute or two. The **Recenter**
button on the phone snaps it back to centre. Dolphin's *Total Yaw* setting (25° in the generated
profiles) sets how far a turn moves the cursor; games vary, so adjust to taste.

What it cannot do: rumble (the DSU protocol has none), the speaker, and Nunchuk motion (one phone,
one set of sensors). Latency stacks phone → service → Phonepads → Dolphin, which is fine for most
motion games and noticeable in the twitchiest.

If the port is taken — DS4Windows, say — Wii Remote mode reports itself unavailable with the reason.
`dsuPort` in `phonepads.settings.json` moves it.

## Publishing a portable executable

```bash
dotnet publish src/Phonepads.App -c Release -p:PublishTrimmed=true -p:DebugType=none -o publish
```

That produces a single self-contained `Phonepads.App.exe` — no .NET runtime install, no installer,
no registry writes.

One caveat worth knowing: `PublishSingleFile` unpacks the bundled native libraries (Skia, HarfBuzz,
ANGLE) to `%TEMP%\.net\` on first launch, which is at odds with the "nothing outside the app folder"
goal in SETUP-1. Setting `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to a folder beside the executable moves
them into the app folder, but it has to be set by whatever launches the exe — the app cannot set it
for itself, because extraction happens before `Main` runs. The alternative is to drop
`IncludeNativeLibrariesForSelfExtract` and ship those three DLLs next to the exe: four files instead
of one, and nothing written outside the folder. That decision is still open.

## Layout

| Project | What lives there |
|---|---|
| `src/Phonepads.Protocol` | Wire types, source-generated JSON, the HTTP setup call and the WebSocket session |
| `src/Phonepads.Core` | Schemas, mappings, the mapping engine, motion integration, session orchestration, portable storage |
| `src/Phonepads.VirtualPads` | `IVirtualPadHub` over ViGEmBus — the Xbox backend |
| `src/Phonepads.Dsu` | `IVirtualPadHub` over a DSU server — the Wii Remote backend — and the Dolphin profile generator |
| `src/Phonepads.App` | Avalonia UI (MVVM) |
| `tests/Phonepads.Tests` | Everything above except the two Windows-only edges (the ViGEm driver and the UI) |

Virtual pads sit behind `IVirtualPadHub`, one hub per backend. A mapping declares its backend; the
session hands each player a pad from the hub their layout needs, and swaps it if they switch layouts.

## Notes on the protocol

The service is in beta and its protocol can change. Two places make that explicit:

- Player objects are parsed defensively — the docs spell the schema and connection fields more than
  one way, so both spellings are accepted and missing fields fall back to sensible defaults.
- The parsing of a snapshot's player list is inferred from the developer guide rather than verified
  against a live session. If players show up unnamed or unassigned, that is the first place to look.

Two protocol rules shape the motion path and are worth knowing if you touch it:

- Motion samples arrive in bursts with their own timestamps, but Dolphin ignores DSU timestamps and
  integrates rates against its own clock. `MotionIntegrator` therefore emits, on each tick, the rate
  that reproduces the angle the phone actually turned through since the last tick — replaying the
  burst would lose most of it.
- The axis signs sent to Dolphin are derived from its DSU client source, not from the protocol
  reference (which does not define them). If a motion looks mirrored, `WiiMotionFrame` is the one
  place to change, and Dolphin's Motion Input tab shows live bars to check against.
