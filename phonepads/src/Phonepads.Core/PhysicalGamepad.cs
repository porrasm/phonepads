namespace Phonepads.Core;

/// <summary>
/// A real controller paired with the phone. The phone bridges it through the browser's
/// Gamepad API and sends the W3C standard layout under fixed ids, in the usual input frames,
/// whatever the controller's brand — so an Xbox, PlayStation or Switch Pro pad all arrive as
/// a/b/x/y in the standard positions (south, east, west, north).
///
/// This is the one control set the app does not define: the phone does. It is described
/// here so the mapping engine can drive a virtual pad from it like any other schema, and so
/// its mapping can be edited like any other. Offering it sets "allowPhysicalGamepad" on the
/// session instead of sending a layout.
/// </summary>
public static class PhysicalGamepad
{
    public const string SchemaId = Schema.PhysicalGamepadId;

    /// <summary>The fixed control set, every id present in every frame.</summary>
    public static Schema Schema { get; } = new()
    {
        Id = SchemaId,
        Name = "Real gamepad",
        IsPreset = true,
        Controls =
        [
            new() { Id = "left-stick", Type = ControlType.Joystick },
            new() { Id = "right-stick", Type = ControlType.Joystick },
            new() { Id = "dpad", Type = ControlType.Joystick, Mode = ControlMode.Dpad },
            new() { Id = "a", Type = ControlType.Button, Label = "A" },
            new() { Id = "b", Type = ControlType.Button, Label = "B" },
            new() { Id = "x", Type = ControlType.Button, Label = "X" },
            new() { Id = "y", Type = ControlType.Button, Label = "Y" },
            new() { Id = "lb", Type = ControlType.Button, Label = "LB" },
            new() { Id = "rb", Type = ControlType.Button, Label = "RB" },
            // The one value shape touch never sends: analog 0..1.
            new() { Id = "lt", Type = ControlType.Trigger, Label = "LT" },
            new() { Id = "rt", Type = ControlType.Trigger, Label = "RT" },
            new() { Id = "back", Type = ControlType.Button, Label = "Back" },
            new() { Id = "start", Type = ControlType.Button, Label = "Start" },
            new() { Id = "ls", Type = ControlType.Button, Label = "LS" },
            new() { Id = "rs", Type = ControlType.Button, Label = "RS" },
            new() { Id = "home", Type = ControlType.Button, Label = "Home" },
        ],
    };

    /// <summary>
    /// The obvious mapping: the standard layout onto the same buttons of an Xbox pad, so a
    /// real controller behaves in the game exactly as if it were plugged into the PC.
    /// </summary>
    public static Mapping DefaultMapping { get; } = new()
    {
        SchemaId = SchemaId,
        Backend = PadBackend.XInput,
        Controls = new Dictionary<string, ControlMapping>(StringComparer.Ordinal)
        {
            ["left-stick"] = new() { Target = PadTarget.LeftStick },
            ["right-stick"] = new() { Target = PadTarget.RightStick },
            ["dpad"] = new() { Target = PadTarget.Dpad },
            ["a"] = new() { Target = PadTarget.A },
            ["b"] = new() { Target = PadTarget.B },
            ["x"] = new() { Target = PadTarget.X },
            ["y"] = new() { Target = PadTarget.Y },
            ["lb"] = new() { Target = PadTarget.LeftBumper },
            ["rb"] = new() { Target = PadTarget.RightBumper },
            ["lt"] = new() { Target = PadTarget.LeftTrigger },
            ["rt"] = new() { Target = PadTarget.RightTrigger },
            ["back"] = new() { Target = PadTarget.Back },
            ["start"] = new() { Target = PadTarget.Start },
            ["ls"] = new() { Target = PadTarget.LeftThumbClick },
            ["rs"] = new() { Target = PadTarget.RightThumbClick },
            ["home"] = new() { Target = PadTarget.Guide },
        },
    };

    public static MappedSchema Mapped { get; } = new(Schema, DefaultMapping);
}
