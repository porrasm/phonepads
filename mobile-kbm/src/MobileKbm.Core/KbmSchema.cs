using Phonepads.Protocol;

namespace MobileKbm.Core;

/// <summary>What a control on the phone does on the PC.</summary>
public abstract record KbmAction;

/// <summary>
/// Holds keys for as long as the button is held: modifiers first, the main key last. With
/// <see cref="Repeat"/> the main key auto-repeats like a real held key.
/// </summary>
public sealed record KeyAction(IReadOnlyList<Key> Keys, bool Repeat = false) : KbmAction
{
    public Key Main => Keys[^1];
}

/// <summary>Holds a mouse button for as long as the phone button is held, so drags work.</summary>
public sealed record MouseButtonAction(MouseButton Button) : KbmAction;

/// <summary>A scroll strip: a finger dragged along it turns the wheel, the content following the finger.</summary>
public sealed record ScrollStripAction : KbmAction;

/// <summary>A "dpad" joystick that holds the arrow keys (two at once on a diagonal).</summary>
public sealed record ArrowPadAction : KbmAction;

/// <summary>
/// A touchpad control or the raw background, read as a laptop touchpad. <see cref="Aspect"/>
/// is the surface's width / height, which turns its 0–1 coordinates into equal-sized steps.
/// </summary>
public sealed record TouchSurfaceAction(double Aspect) : KbmAction;

/// <summary>A text control: whatever the player sends is typed out.</summary>
public sealed record TextAction : KbmAction;

public sealed record KbmControl(ControlDto Dto, KbmAction Action);

/// <summary>A phone layout together with what each of its controls does.</summary>
public sealed class KbmSchema
{
    private readonly Dictionary<string, KbmAction> _actions;

    public KbmSchema(string id, string name, string orientation, IReadOnlyList<KbmControl> controls)
    {
        Id = id;
        Name = name;
        Orientation = orientation;
        Controls = controls;
        _actions = new Dictionary<string, KbmAction>(StringComparer.Ordinal);
        foreach (var control in controls) _actions.TryAdd(control.Dto.Id, control.Action);
    }

    public string Id { get; }

    public string Name { get; }

    public string Orientation { get; }

    public IReadOnlyList<KbmControl> Controls { get; }

    public KbmAction? ActionFor(string controlId) => _actions.GetValueOrDefault(controlId);

    public SchemaDto ToDto() => new()
    {
        Id = Id,
        Name = Name,
        Orientation = Orientation,
        Controls = Controls.Select(c => c.Dto).ToList(),
    };

    /// <summary>The service's own rules, checked here so a mistake shows up in a test, not as a 400.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (!IsKebabCase(Id)) problems.Add($"Schema id '{Id}' must be kebab-case.");
        if (Controls.Count is < 1 or > 16)
            problems.Add($"'{Id}' needs 1 to 16 controls; it has {Controls.Count}.");

        foreach (var group in Controls.GroupBy(c => c.Dto.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            problems.Add($"'{Id}' uses control id '{group.Key}' more than once.");

        foreach (var control in Controls.Where(c => !IsKebabCase(c.Dto.Id)))
            problems.Add($"Control id '{control.Dto.Id}' in '{Id}' must be kebab-case.");

        if (Controls.Count(c => c.Dto.Type == "raw") > 1)
            problems.Add($"'{Id}' has more than one raw surface; the service allows one.");

        foreach (var control in Controls.Where(c => c.Dto.Aspect is < 0.25 or > 4))
            problems.Add($"Touchpad '{control.Dto.Id}' in '{Id}' needs an aspect between 0.25 and 4.");

        foreach (var control in Controls.Where(c => c.Dto.X is < 0 or > 100 || c.Dto.Y is < 0 or > 100))
            problems.Add($"Control '{control.Dto.Id}' in '{Id}' is positioned outside 0–100 %.");

        // The raw background has no position; everything else is placed all together or not at all.
        var placeable = Controls.Where(c => c.Dto.Type != "raw").ToList();
        var placed = placeable.Count(c => c.Dto.X is not null && c.Dto.Y is not null);
        if (placed != 0 && placed != placeable.Count)
            problems.Add($"'{Id}' positions some controls but not all; the phone expects every control or none.");

        return problems;
    }

    internal static bool IsKebabCase(string value) =>
        value.Length > 0
        && value[0] is >= 'a' and <= 'z'
        && value[^1] != '-'
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}

/// <summary>A control's exact centre, in percent of the controller's width and height from the top-left.</summary>
public readonly record struct At(double X, double Y);

/// <summary>Declares a schema one control at a time, keeping each control next to what it does.</summary>
public sealed class SchemaBuilder(string id, string name, string orientation)
{
    private readonly List<KbmControl> _controls = [];

    /// <summary>A laid-out touch surface with a known shape — the pointer when other controls share the screen.</summary>
    public SchemaBuilder Touchpad(string controlId, double aspect, string? size = null, At? at = null) =>
        Add(new ControlDto { Type = "touchpad", Id = controlId, Aspect = aspect, Size = size, X = at?.X, Y = at?.Y },
            new TouchSurfaceAction(aspect));

    /// <summary>
    /// The whole background as a touch surface. The phone does not say how big the box is, so
    /// <paramref name="assumedAspect"/> stands in for its width / height.
    /// </summary>
    public SchemaBuilder RawSurface(string controlId, double assumedAspect) =>
        Add(new ControlDto { Type = "raw", Id = controlId }, new TouchSurfaceAction(assumedAspect));

    public SchemaBuilder Keys(string controlId, string label, KeyAction action, string? size = null, string? zone = null, At? at = null) =>
        Add(new ControlDto { Type = "button", Id = controlId, Label = label, Shape = "rect", Size = size, Zone = zone, X = at?.X, Y = at?.Y },
            action);

    public SchemaBuilder Mouse(string controlId, string label, MouseButton button, string? size = null, At? at = null) =>
        Add(new ControlDto { Type = "button", Id = controlId, Label = label, Shape = "rect", Size = size, X = at?.X, Y = at?.Y },
            new MouseButtonAction(button));

    /// <summary>A tall, narrow touchpad that scrolls as a finger is dragged along it.</summary>
    public SchemaBuilder ScrollStrip(string controlId, At? at = null) =>
        Add(new ControlDto
            {
                Type = "touchpad", Id = controlId, Label = "Scroll", Aspect = ScrollStripAspect, Size = "large",
                X = at?.X, Y = at?.Y,
            },
            new ScrollStripAction());

    public SchemaBuilder ArrowPad(string controlId, string? size = null, At? at = null) =>
        Add(new ControlDto { Type = "joystick", Id = controlId, Mode = "dpad", Size = size, X = at?.X, Y = at?.Y },
            new ArrowPadAction());

    public SchemaBuilder Text(string controlId, string label, string? size = null, At? at = null) =>
        Add(new ControlDto
            {
                Type = "text", Id = controlId, Label = label, Shape = "rect", Size = size, MaxLength = 1000,
                X = at?.X, Y = at?.Y,
            },
            new TextAction());

    /// <summary>Width / height of a scroll strip: the narrowest the service allows.</summary>
    internal const double ScrollStripAspect = 0.25;

    public KbmSchema Build() => new(id, name, orientation, _controls.ToList());

    private SchemaBuilder Add(ControlDto dto, KbmAction action)
    {
        _controls.Add(new KbmControl(dto, action));
        return this;
    }
}
