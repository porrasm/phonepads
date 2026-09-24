using Phonepads.Protocol;

namespace MobileKbm.Core;

/// <summary>
/// The layouts the phone can pick from. Deliberately not a full keyboard: they cover what a
/// phone is good at (pointing, scrolling, typing a line, the usual shortcuts). Anything more
/// exotic is one tap away through Windows' own on-screen keyboard (the "Keyboard" button).
/// </summary>
public static class KbmSchemas
{
    /// <summary>
    /// Shipped with the app, never changed: the phone files each player's edited layouts under
    /// it, so a rearranged Mouse layout survives every recreated session.
    /// </summary>
    public const string DriverAppUuid = "b6ec5d52-4ad1-4950-b1b1-5a00a2482a11";

    /// <summary>
    /// Width / height assumed for a portrait controller box. Raw touch reports 0–1 of each
    /// side without saying how long the sides are; phones land around 0.5–0.6 once the header
    /// is taken off, and being a little off only makes one axis a little faster.
    /// </summary>
    public const double PortraitBoxAspect = 0.55;

    private static KeyAction Chord(params Key[] keys) => new(keys);

    private static KeyAction Repeating(params Key[] keys) => new(keys, Repeat: true);

    /// <summary>Win+Ctrl+O toggles Windows' On-Screen Keyboard — the escape hatch for everything else.</summary>
    private static readonly KeyAction OnScreenKeyboard = Chord(Key.Win, Key.Ctrl, Key.O);

    /// <summary>
    /// Pointer, both buttons, a scroll stick, a line of text and the keys that follow typing.
    /// The touchpad is a laid-out control rather than the raw background because its aspect
    /// is declared, so pointer speed is the same in both directions whatever the phone.
    /// </summary>
    public static KbmSchema Mouse { get; } = new SchemaBuilder("mouse", "Mouse", "portrait")
        .Touchpad("pad", aspect: 1.2, size: "large")
        .Mouse("left", "Left", MouseButton.Left)
        .Mouse("right", "Right", MouseButton.Right)
        .ScrollStick("scroll")
        .Text("type", "Type")
        .Keys("enter", "Enter", Chord(Key.Enter))
        .Keys("backspace", "⌫", Repeating(Key.Backspace))
        .Keys("esc", "Esc", Chord(Key.Escape))
        .Keys("keyboard", "Keyboard", OnScreenKeyboard, zone: "aux")
        .Build();

    /// <summary>
    /// The whole screen as a laptop touchpad and nothing else: slide to point, double-tap to
    /// click, double-tap-and-slide to drag, two fingers to scroll, two-finger tap to right-click.
    /// </summary>
    public static KbmSchema Touchpad { get; } = new SchemaBuilder("touchpad", "Touchpad only", "portrait")
        .RawSurface("surface", PortraitBoxAspect)
        .Build();

    /// <summary>Browsing from the sofa: address bar, tabs, back and forward, plus the pointer.</summary>
    public static KbmSchema Browser { get; } = new SchemaBuilder("browser", "Browser", "portrait")
        .Touchpad("pad", aspect: 1.2, size: "large")
        .Mouse("left", "Left", MouseButton.Left)
        .ScrollStick("scroll")
        .Text("type", "Type")
        .Keys("enter", "Enter", Chord(Key.Enter))
        .Keys("back", "◀ Back", Chord(Key.BrowserBack))
        .Keys("forward", "Fwd ▶", Chord(Key.BrowserForward))
        .Keys("address", "Address", Chord(Key.Ctrl, Key.L))
        .Keys("new-tab", "New tab", Chord(Key.Ctrl, Key.T))
        .Keys("close-tab", "Close tab", Chord(Key.Ctrl, Key.W))
        .Keys("next-tab", "Next tab", Chord(Key.Ctrl, Key.Tab))
        .Keys("reload", "Reload", Chord(Key.F5))
        .Keys("esc", "Esc", Chord(Key.Escape))
        .Mouse("right", "Right", MouseButton.Right)
        .Build();

    /// <summary>Navigation and editing keys and the everyday Windows shortcuts — no pointer.</summary>
    public static KbmSchema Keys { get; } = new SchemaBuilder("keys", "Keys", "portrait")
        .ArrowPad("arrows", size: "large")
        .Keys("enter", "Enter", Chord(Key.Enter))
        .Keys("esc", "Esc", Chord(Key.Escape))
        .Keys("tab", "Tab", Repeating(Key.Tab))
        .Keys("backspace", "⌫", Repeating(Key.Backspace))
        .Keys("delete", "Del", Repeating(Key.Delete))
        .Keys("space", "Space", Chord(Key.Space))
        .Text("type", "Type")
        .Keys("alt-tab", "Alt+Tab", Chord(Key.Alt, Key.Tab))
        .Keys("start", "Start", Chord(Key.Win))
        .Keys("copy", "Copy", Chord(Key.Ctrl, Key.C))
        .Keys("paste", "Paste", Chord(Key.Ctrl, Key.V))
        .Keys("undo", "Undo", Chord(Key.Ctrl, Key.Z))
        .Keys("desktop", "Desktop", Chord(Key.Win, Key.D))
        .Keys("close-window", "Close window", Chord(Key.Alt, Key.F4))
        .Keys("keyboard", "Keyboard", OnScreenKeyboard, zone: "aux")
        .Build();

    /// <summary>
    /// A media remote for a PC on the TV. The media keys work in any player that listens to
    /// them (browsers included); the arrows seek and F toggles full screen in YouTube, Netflix,
    /// VLC and most web players.
    /// </summary>
    public static KbmSchema Media { get; } = new SchemaBuilder("media", "Media", "portrait")
        .Keys("play-pause", "Play / Pause", Chord(Key.MediaPlayPause), size: "large")
        .Keys("seek-back", "« Seek", Repeating(Key.Left))
        .Keys("seek-forward", "Seek »", Repeating(Key.Right))
        .Keys("volume-down", "Vol −", Repeating(Key.VolumeDown))
        .Keys("volume-up", "Vol +", Repeating(Key.VolumeUp))
        .Keys("mute", "Mute", Chord(Key.VolumeMute))
        .Keys("fullscreen", "Full screen", Chord(Key.F))
        .Keys("previous", "⏮ Prev", Chord(Key.MediaPrevious))
        .Keys("next", "Next ⏭", Chord(Key.MediaNext))
        .Keys("esc", "Esc", Chord(Key.Escape))
        .Touchpad("pad", aspect: 1.5)
        .Mouse("click", "Click", MouseButton.Left)
        .Build();

    /// <summary>
    /// A presentation clicker. Page Down / Page Up are what hardware clickers send, and every
    /// slide program and PDF viewer understands them.
    /// </summary>
    public static KbmSchema Slides { get; } = new SchemaBuilder("slides", "Slides", "portrait")
        .Keys("next", "Next ▶", Chord(Key.PageDown), size: "large")
        .Keys("previous", "◀ Previous", Chord(Key.PageUp))
        .Keys("start", "Start show (F5)", Chord(Key.F5))
        .Keys("black", "Black screen", Chord(Key.B))
        .Keys("end", "End show", Chord(Key.Escape))
        .Build();

    /// <summary>Every layout, the default first.</summary>
    public static IReadOnlyList<KbmSchema> All { get; } = [Mouse, Touchpad, Browser, Keys, Media, Slides];

    /// <summary>
    /// The session every run asks for. Always private — this is a remote control for a PC, so
    /// only the key's owner (and the emails linked to the key) may join, never a join code —
    /// and always without a lobby: it starts at once and stays open to phones coming and going.
    /// </summary>
    public static SessionConfig CreateSessionConfig(string machineName) => new()
    {
        Game = Truncate(machineName, 32) + " — mouse & keyboard",
        DriverAppUuid = DriverAppUuid,
        Private = true,
        SkipLobby = true,
        // One person, a few devices (a phone and a tablet, say). No point in more.
        MaxPlayers = 4,
        Schemas = All.Select(s => s.ToDto()).ToList(),
    };

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
