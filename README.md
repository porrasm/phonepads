# Phonepads

Two Windows apps that are **drivers** for the [Gamepad service](https://gamepad.porras.club): phones
connect to the service, and these apps turn what the phones send into input on the PC.

| App | What it is |
|---|---|
| [`phonepads/`](phonepads/README.md) | Phones as game controllers: virtual Xbox pads (ViGEmBus) and Wii Remotes for Dolphin. A windowed app with a lobby, layouts and mappings. |
| [`mobile-kbm/`](mobile-kbm/README.md) | The phone as the PC's mouse and keyboard. A tray app that stays connected on its own — set a driver key once and forget it. |

Both build on [`shared/Phonepads.Protocol`](shared/Phonepads.Protocol), the wire layer for the
service's HTTP and WebSocket protocol. The protocol itself is documented in [protocol.txt](protocol.txt).

## Building

Everything needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and builds on Windows.

```bash
dotnet build All.slnx
dotnet test All.slnx
```

Each app also has its own solution (`phonepads/Phonepads.slnx`, `mobile-kbm/MobileKbm.slnx`) that
includes the shared project.
