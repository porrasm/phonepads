# Phonepads — Specification

A portable Windows desktop app that turns phones into real game controllers.

It acts as a **driver** for the [Gamepad service](https://gamepad.porras.club): it claims a session,
shows a join code, receives phone input over WebSocket, and translates that input into
**virtual Xbox 360 controllers** that any Windows game can use.

Status: draft. User-story level; technical detail is deliberately minimal.

---

## 1. Concepts

Three different things get called "controls" in this domain, so the spec uses these terms strictly:

| Term | Meaning |
|---|---|
| **Session** | One game of the Gamepad service. Created by a host on the website, claimed by this app. |
| **Schema** | A phone-side control layout — which on-screen controls a player sees (joysticks, buttons, dpad, gyro). Defined by the service protocol. |
| **Mapping** | How a schema's controls translate into virtual gamepad inputs. Owned by this app; the service knows nothing about it. |
| **Player** | One phone connected to the session. Has a name, colour, and chosen schema. |
| **Virtual pad** | An emulated Xbox 360 controller this app creates on Windows. |

The core data flow:

```
Phone → [Gamepad service] → Phonepads → Mapping → Virtual pad → Game
```

---

## 2. Scope

**In scope**

- Portable Windows app, no installation
- Selecting, creating and editing schemas
- Mapping schema controls to virtual gamepad inputs
- Running a session: join code, player lobby, start / pause / terminate
- Up to 4 simultaneous virtual pads

**Out of scope (for now)**

- Hosting sessions (the user creates the session on the website and pastes a setup code)
- An online community library of shared schemas — sharing is file-based
- macOS / Linux
- Keyboard and mouse emulation

---

## 3. User stories

Priority: **M** must-have · **S** should-have · **C** could-have

### 3.1 Setup and portability

**SETUP-1 (M) — Run without installing**
*As a user, I want to run the app straight from a folder or USB stick, so that I don't need admin rights or an installer.*

- Single executable; no installation step, no admin rights required to run
- All data (schemas, mappings, settings) is stored beside the executable
- Nothing is written to the registry, `%AppData%`, or anywhere outside the app folder
- Moving or copying the app folder carries all user data with it

**SETUP-2 (M) — Understand the driver prerequisite**
*As a user, I want to be told clearly if the controller driver is missing, so that I know what to do about it.*

- The app requires **ViGEmBus** to be installed on the machine. This is a signed kernel driver and is the one thing that is **not** portable — it is a documented prerequisite, installed by the user, once per machine.
- On startup the app checks whether the driver is present
- If absent: a clear, non-technical explanation, the download link, and the app stays usable for everything except actually creating pads (schemas and mappings can still be edited)
- The app never attempts to install the driver itself

**SETUP-3 (S) — Know when a game won't accept virtual pads**
*As a user, I want to understand why some games ignore my controllers, so that I don't think the app is broken.*

- Documented limitation: kernel anti-cheat systems (e.g. Vanguard, some EAC/BattlEye titles) deliberately block virtual pads
- Surfaced as help content, not as a runtime check

### 3.2 Starting a session

**SESSION-1 (M) — Claim a session with a setup code**
*As a user, I want to paste the setup code from the website, so that my app becomes the game's controller source.*

- Single obvious input for the 6-character setup code
- A shortcut to open the session-creation page in the browser
- On success: the app shows the join code prominently, plus a QR code of the join URL for players to scan
- Clear, friendly errors for: unknown or already-used code, network failure, rate limiting
- The setup code is single-use; the app stores the resulting driver token so it can reconnect

**SESSION-2 (M) — Choose what players can pick**
*As a user, I want to choose which schemas this session offers, so that players get controls suited to the game.*

- Before claiming, the user selects **1–4 schemas** to offer
- If more than one is offered, players choose between them on their phones
- The user can name the game (shown to players) and set min/max players

**SESSION-3 (M) — Reconnect after a drop**
*As a user, I want the app to recover from a lost connection, so that a brief network blip doesn't end our game.*

- Automatic reconnection with backoff using the stored driver token
- Terminal conditions (dead token, session ended, unauthorised) are reported plainly instead of retried
- On reconnect the session state is refreshed from the server and the user is prompted to resume

### 3.3 Schemas

**SCHEMA-1 (M) — Pick a ready-made schema**
*As a user, I want to choose from a library of common layouts, so that I can start playing without designing anything.*

- The app ships with a bundled preset library covering common game types, each already mapped to a sensible default:

| Preset | Phone controls |
|---|---|
| Generic Gamepad | Two joysticks, four face buttons, two shoulders |
| Twin-Stick Shooter | Two full joysticks, fire button |
| Racing | Steering (tilt or slider), accelerate / brake |
| Platformer | Dpad, jump, action |
| Fighting | Dpad, six buttons |
| Party / Minimal | Dpad and one big button |

- Each preset shows a visual preview of the phone layout before it is chosen
- Presets are read-only; choosing "edit" creates an editable copy

**SCHEMA-2 (M) — Create a schema**
*As a user, I want to build my own layout, so that I can match a game's specific needs.*

- Add, remove and reorder controls; order expresses importance and drives phone layout
- Supported control types: **joystick** (full / x-only / y-only / dpad), **button**, **gyro** (tilt)
- Per-control optional hints: zone (left, right, shoulder-left, shoulder-right, aux) and size (small, medium, large)
- Per-schema orientation hint: auto, landscape, portrait
- Live preview of the resulting phone layout while editing
- Validation with inline messages: 1–16 controls, unique control ids, valid id format

**SCHEMA-3 (M) — Edit and duplicate**
*As a user, I want to tweak an existing schema, so that I don't start from scratch.*

- Duplicate any schema, preset or custom
- Rename, edit and delete custom schemas
- Editing a schema warns if it would break an existing mapping (see MAP-4)

**SCHEMA-4 (S) — Share schemas as files**
*As a user, I want to export and import schemas, so that I can share them with friends or move them between machines.*

- Export one or more schemas (with their mappings) to a single file
- Import with a preview of what will be added and conflict handling for duplicate names
- Human-readable file format so schemas can be shared in a chat or a gist

**SCHEMA-5 (C) — Gyro fallback**
*As a user, I want a non-tilt alternative offered, so that players on devices without motion sensors can still join.*

- Schemas containing a gyro control are flagged; the app warns if every offered schema requires gyro
- Suggests offering at least one gyro-free schema alongside

### 3.4 Mapping

**MAP-1 (M) — Map schema controls to gamepad inputs**
*As a user, I want to decide what each phone control does on the virtual gamepad, so that it works with the game I'm playing.*

- A mapping editor lists every control in the schema alongside its assigned gamepad target
- Available targets: left/right thumbstick (as a pair or a single axis), left/right trigger, A/B/X/Y, LB/RB, Back/Start, thumbstick clicks, dpad directions
- Sensible target types are offered per control type (e.g. a full joystick offers thumbsticks and dpad; a button offers buttons and triggers)
- Unmapped controls are allowed and simply do nothing

**MAP-2 (M) — One mapping per schema**
*As a user, I want a schema's mapping to apply to everyone using it, so that there's one thing to configure and it's predictable.*

- A mapping belongs to its schema and is shared by every player who selects that schema
- No per-player mapping overrides

**MAP-3 (M) — Adjust feel**
*As a user, I want to correct axis behaviour, so that controls don't feel wrong or inverted.*

- Per-axis invert
- Deadzone and sensitivity per analogue control
- Gyro range (degrees of tilt for full deflection)

**MAP-4 (S) — Test a mapping without a game**
*As a user, I want to see what my phone input is doing, so that I can fix a mapping without alt-tabbing out of a game.*

- A live view showing incoming phone input and the resulting virtual pad state side by side
- Works with a connected phone during the lobby, before the game starts
- Clearly flags controls that are unmapped or mapped to an already-used target

### 3.5 Playing

**PLAY-1 (M) — Watch the lobby**
*As a user, I want to see who has joined, so that I know when we're ready to start.*

- Live list of players with name, colour, chosen schema, connection state and ready state
- A prominent join code and QR code while the lobby is open
- A clear summary such as "3 of 4 ready"

**PLAY-2 (M) — Assign players to gamepads**
*As a user, I want control over which player is which gamepad, so that player 1 in the game is the person I expect.*

- Each connected player is assigned a virtual pad slot, 1 to 4
- Default assignment is join order; the user can reorder
- **Windows supports at most 4 XInput controllers.** Players beyond the fourth are shown as unassigned and receive no pad; the app explains why rather than failing silently.

**PLAY-3 (M) — Start the game**
*As a user, I want to start when we're ready, so that the game begins on my terms and not automatically.*

- Starting is always an explicit user action, never triggered by everyone being ready
- On start, virtual pads are created for assigned players and input begins flowing

**PLAY-4 (M) — Pause and resume**
*As a user, I want to pause the session, so that we can take a break without losing our places.*

- A pause control that stops input reaching the game
- While paused, all virtual pads are released to a neutral state so nothing is left stuck down
- Players see that the session is paused; they keep their slots
- Resume is an explicit action

**PLAY-5 (M) — Terminate the session**
*As a user, I want to end the session cleanly, so that no virtual controllers are left behind.*

- A terminate control, with confirmation
- On terminate: the service session is ended, all virtual pads are removed, and the app returns to its idle state
- The same cleanup runs if the app is closed or crashes, so no orphaned pads survive

**PLAY-6 (M) — Survive players coming and going**
*As a user, I want disconnections handled gracefully, so that one person's bad wifi doesn't disrupt everyone.*

- A player who drops keeps their slot; their pad returns to neutral until they return
- A player who leaves permanently frees their slot, which can then be given to a waiting player
- Connection state is always visible per player

**PLAY-7 (S) — Rumble back to the phone**
*As a user, I want my phone to vibrate when the game rumbles, so that it feels like a real controller.*

- Force-feedback from the game to a virtual pad is forwarded to the corresponding player's phone as a vibration
- Can be disabled globally

---

## 4. Constraints

These are fixed by the platform or the service protocol, not by choice:

- **4 virtual pads maximum** — Windows XInput limit, though the service allows more players
- **1–4 schemas per session**, **1–16 controls per schema**, unique control ids
- **Phone lays out its own controls** — the app describes what controls exist and their relative importance, never their pixel positions
- **Movement arrives at ~30 fps**; button and dpad changes arrive immediately
- **Axis convention differs**: the service uses `[-1, 1]` with Y positive *downwards*; gamepads use Y positive *upwards*. The mapping layer is responsible for this conversion.
- **Only the app can start, pause, resume and end** the session; the website host's only power is ending it
- **A session is claimed once** — the setup code is consumed, and the driver token is the only way back in

---

## 5. Minimal technical notes

Deliberately brief; detailed design lives elsewhere.

- **Platform**: Windows 10/11 x64. C# / .NET 10.
- **UI**: Avalonia with MVVM.
- **Virtual pads**: ViGEmBus via its .NET client, behind an internal interface so the backend can be replaced (its successor, VirtualPad, or a future Linux backend) without touching the rest of the app.
- **Service client**: plain HTTPS for setup and a WebSocket for the session. No SDK required.
- **Storage**: human-readable files beside the executable — one for settings, one library file for schemas and mappings.
- **Portability**: single-file, self-contained publish so no .NET runtime install is needed.

---

## 6. Open questions

1. **Players beyond four.** Should the app offer DualShock 4 emulation as an alternative backend for games that read DirectInput, allowing more than four players? Or is "4 max" an accepted limit?
2. **Trigger fidelity.** Phone buttons are boolean, but triggers are analogue. Should a button mapped to a trigger produce full press only, or should analogue targets require an analogue source?
3. **Multiple controls to one target.** Should two schema controls be allowed to map to the same gamepad input (e.g. both a button and a tilt driving the same axis)? Currently flagged as a warning.
4. **Preset library updates.** If bundled presets improve in a later version, what happens to a user's edited copies?
5. **Gyro recentring.** The phone recentres its neutral pose on tap. Should the app expose anything for this, or is it purely phone-side?
