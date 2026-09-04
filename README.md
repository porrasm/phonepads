# Phonepads

A portable Windows app that turns phones into real game controllers.

It is a **driver** for the [Gamepad service](https://gamepad.porras.club): it claims a session with a
setup code, shows a join code for players to scan, receives their input over a WebSocket, and turns
that input into virtual Xbox 360 controllers any Windows game can read.

```
Phone → [Gamepad service] → Phonepads → Mapping → Virtual pad → Game
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
- Seven bundled preset layouts, each with a default mapping, including a single-stick GameCube
  layout aimed at Dolphin
- Input mapping to virtual Xbox 360 pads, including the y-axis flip between phone and pad
- Live per-player pad readout, so a mapping can be checked without a game running
- Automatic reconnection with backoff; terminal close codes are reported instead of retried
- Portable settings stored beside the executable

Not built yet: the schema editor, the mapping editor, schema import/export, and reconnecting to an
existing session with a stored driver token.

## Requirements

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) to create controllers

ViGEmBus is a signed kernel driver, installed once per machine. It is the one part of the stack that
is not portable. Without it the app still runs — it just cannot create pads, and says so.

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

## Publishing a portable executable

```bash
dotnet publish src/Phonepads.App -c Release -p:PublishTrimmed=true -p:DebugType=none -o publish
```

That produces a single self-contained `Phonepads.exe` — no .NET runtime install, no installer, no
registry writes.

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
| `src/Phonepads.Core` | Schemas, mappings, the mapping engine, session orchestration, portable storage |
| `src/Phonepads.VirtualPads` | `IVirtualPadHub` implemented over ViGEmBus |
| `src/Phonepads.App` | Avalonia UI (MVVM) |
| `tests/Phonepads.Tests` | Mapping engine, protocol parsing and schema validation |

Virtual pads sit behind `IVirtualPadHub` so the ViGEmBus backend can be swapped for its successor,
or a future non-Windows backend, without touching anything above it.

## Notes on the protocol

The service is in beta and its protocol can change. Two places make that explicit:

- Player objects are parsed defensively — the docs spell the schema and connection fields more than
  one way, so both spellings are accepted and missing fields fall back to sensible defaults.
- The parsing of a snapshot's player list is inferred from the developer guide rather than verified
  against a live session. If players show up unnamed or unassigned, that is the first place to look.
