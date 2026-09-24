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

    // Every layout places its controls itself (x / y, in percent of the portrait screen). Left
    // to the phone, the layout engine fills from the right thumb outwards, which puts a "Left"
    // button on the right, and it never makes the pointer area bigger than one box.

    /// <summary>
    /// The everyday layout: the whole background is the touchpad, the mouse buttons sit along
    /// the bottom where the thumb is, a scroll strip runs down the right edge, and the typing
    /// keys line the top.
    /// </summary>
    public static KbmSchema Mouse { get; } = new SchemaBuilder("mouse", "Mouse", "portrait")
        .RawSurface("surface", PortraitBoxAspect)
        .Mouse("left", "Left", MouseButton.Left, size: "large", at: new(22, 91))
        .Mouse("right", "Right", MouseButton.Right, size: "large", at: new(64, 91))
        .ScrollStrip("scroll", at: new(91, 55))
        .Text("type", "Type", at: new(10, 6))
        .Keys("enter", "Enter", Chord(Key.Enter), at: new(30, 6))
        .Keys("backspace", "⌫", Repeating(Key.Backspace), at: new(50, 6))
        .Keys("esc", "Esc", Chord(Key.Escape), at: new(70, 6))
        .Keys("keyboard", "All keys", OnScreenKeyboard, at: new(90, 6))
        .Build();

    /// <summary>
    /// The whole screen as a laptop touchpad and nothing else: slide to point, double-tap to
    /// click, double-tap-and-slide to drag, two fingers to scroll, two-finger tap to right-click.
    /// </summary>
    public static KbmSchema Touchpad { get; } = new SchemaBuilder("touchpad", "Touchpad only", "portrait")
        .RawSurface("surface", PortraitBoxAspect)
        .Build();

    /// <summary>
    /// Browsing from the sofa: the Mouse layout with back, forward, tabs and the address bar along
    /// the top. Kept to what fits: the phone shrinks every button as their number grows.
    /// </summary>
    public static KbmSchema Browser { get; } = new SchemaBuilder("browser", "Browser", "portrait")
        .RawSurface("surface", PortraitBoxAspect)
        .Mouse("left", "Left", MouseButton.Left, size: "large", at: new(22, 91))
        .Mouse("right", "Right", MouseButton.Right, size: "large", at: new(64, 91))
        .ScrollStrip("scroll", at: new(91, 58))
        .Keys("back", "Back", Chord(Key.BrowserBack), at: new(13, 6))
        .Keys("forward", "Fwd", Chord(Key.BrowserForward), at: new(38, 6))
        .Keys("next-tab", "Next tab", Chord(Key.Ctrl, Key.Tab), at: new(63, 6))
        .Keys("close-tab", "Close tab", Chord(Key.Ctrl, Key.W), at: new(88, 6))
        .Keys("address", "URL", Chord(Key.Ctrl, Key.L), at: new(20, 19))
        .Text("type", "Type", at: new(50, 19))
        .Keys("enter", "Enter", Chord(Key.Enter), at: new(80, 19))
        .Build();

    /// <summary>Arrows and the editing keys, big enough to hit without looking.</summary>
    public static KbmSchema Keys { get; } = new SchemaBuilder("keys", "Keys", "portrait")
        .ArrowPad("arrows", size: "large", at: new(50, 28))
        .Keys("enter", "Enter", Chord(Key.Enter), size: "large", at: new(14, 64))
        .Keys("esc", "Esc", Chord(Key.Escape), size: "large", at: new(38, 64))
        .Keys("backspace", "⌫", Repeating(Key.Backspace), size: "large", at: new(62, 64))
        .Keys("delete", "Del", Repeating(Key.Delete), size: "large", at: new(86, 64))
        .Keys("tab", "Tab", Repeating(Key.Tab), size: "large", at: new(14, 86))
        .Keys("space", "Space", Chord(Key.Space), size: "large", at: new(38, 86))
        .Text("type", "Type", size: "large", at: new(62, 86))
        .Keys("keyboard", "All keys", OnScreenKeyboard, size: "large", at: new(86, 86))
        .Build();

    /// <summary>The everyday Windows shortcuts. Apart from Keys because the phone shrinks buttons as their number grows.</summary>
    public static KbmSchema Shortcuts { get; } = new SchemaBuilder("shortcuts", "Shortcuts", "portrait")
        .Keys("copy", "Copy", Chord(Key.Ctrl, Key.C), size: "large", at: new(20, 14))
        .Keys("paste", "Paste", Chord(Key.Ctrl, Key.V), size: "large", at: new(50, 14))
        .Keys("cut", "Cut", Chord(Key.Ctrl, Key.X), size: "large", at: new(80, 14))
        .Keys("undo", "Undo", Chord(Key.Ctrl, Key.Z), size: "large", at: new(20, 38))
        .Keys("redo", "Redo", Chord(Key.Ctrl, Key.Y), size: "large", at: new(50, 38))
        .Keys("select-all", "Select all", Chord(Key.Ctrl, Key.A), size: "large", at: new(80, 38))
        .Keys("alt-tab", "Alt+Tab", Chord(Key.Alt, Key.Tab), size: "large", at: new(20, 62))
        .Keys("start", "Start", Chord(Key.Win), size: "large", at: new(50, 62))
        .Keys("desktop", "Desktop", Chord(Key.Win, Key.D), size: "large", at: new(80, 62))
        .Keys("close-window", "Close window", Chord(Key.Alt, Key.F4), size: "large", at: new(20, 86))
        .Keys("save", "Save", Chord(Key.Ctrl, Key.S), size: "large", at: new(50, 86))
        .Keys("keyboard", "All keys", OnScreenKeyboard, size: "large", at: new(80, 86))
        .Build();

    /// <summary>
    /// A media remote for a PC on the TV. The media keys work in any player that listens to
    /// them (browsers included); the arrows seek and F toggles full screen in YouTube, Netflix,
    /// VLC and most web players. The background is a touchpad for clicking around a web player.
    /// </summary>
    public static KbmSchema Media { get; } = new SchemaBuilder("media", "Media", "portrait")
        .RawSurface("surface", PortraitBoxAspect)
        .Keys("play-pause", "Play / Pause", Chord(Key.MediaPlayPause), size: "large", at: new(50, 12))
        .Keys("previous", "Prev", Chord(Key.MediaPrevious), at: new(18, 12))
        .Keys("next", "Next", Chord(Key.MediaNext), at: new(82, 12))
        .Keys("seek-back", "« Seek", Repeating(Key.Left), at: new(18, 32))
        .Keys("fullscreen", "Full screen", Chord(Key.F), at: new(50, 32))
        .Keys("seek-forward", "Seek »", Repeating(Key.Right), at: new(82, 32))
        .Keys("volume-down", "Vol −", Repeating(Key.VolumeDown), at: new(18, 52))
        .Keys("mute", "Mute", Chord(Key.VolumeMute), at: new(50, 52))
        .Keys("volume-up", "Vol +", Repeating(Key.VolumeUp), at: new(82, 52))
        .Keys("esc", "Esc", Chord(Key.Escape), at: new(18, 91))
        .Mouse("click", "Click", MouseButton.Left, size: "large", at: new(70, 91))
        .Build();

    /// <summary>
    /// A presentation clicker. Page Down / Page Up are what hardware clickers send, and every
    /// slide program and PDF viewer understands them.
    /// </summary>
    public static KbmSchema Slides { get; } = new SchemaBuilder("slides", "Slides", "portrait")
        .Keys("next", "Next", Chord(Key.PageDown), size: "large", at: new(50, 30))
        .Keys("previous", "Previous", Chord(Key.PageUp), size: "large", at: new(50, 62))
        .Keys("start", "Start show (F5)", Chord(Key.F5), at: new(18, 90))
        .Keys("black", "Black screen", Chord(Key.B), at: new(50, 90))
        .Keys("end", "End show", Chord(Key.Escape), at: new(82, 90))
        .Build();

    /// <summary>Every layout, the default first.</summary>
    public static IReadOnlyList<KbmSchema> All { get; } = [Mouse, Touchpad, Browser, Keys, Shortcuts, Media, Slides];

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
