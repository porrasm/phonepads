namespace Phonepads.Core;

/// <summary>A schema together with the mapping it ships with.</summary>
public sealed record MappedSchema(Schema Schema, Mapping Mapping);

/// <summary>
/// The bundled preset library (SCHEMA-1). Presets are read-only; the editor will make a
/// copy rather than change one in place.
/// </summary>
public static class Presets
{
    public static IReadOnlyList<MappedSchema> All { get; } =
    [
        GenericGamepad(),
        GameCube(),
        WiiRemote(),
        WiiRemoteNunchuk(),
        WiiRemoteSideways(),
        TwinStick(),
        Racing(),
        Platformer(),
        Fighting(),
        Party(),
    ];

    public static MappedSchema Default => All[0];

    public static MappedSchema? ById(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Schema.Id, id, StringComparison.Ordinal));

    private static MappedSchema GenericGamepad()
    {
        var schema = new Schema
        {
            Id = "generic-gamepad",
            Name = "Generic Gamepad",
            IsPreset = true,
            Controls =
            [
                Stick("move", ControlZone.Left, ControlSize.Large),
                Stick("look", ControlZone.Right, ControlSize.Large),
                Button("a", "A"),
                Button("b", "B"),
                Button("x", "X"),
                Button("y", "Y"),
                Button("lb", "LB", ControlZone.ShoulderLeft),
                Button("rb", "RB", ControlZone.ShoulderRight),
            ],
        };

        return new MappedSchema(schema, Map("generic-gamepad", PadBackend.XInput, new()
        {
            ["move"] = PadTarget.LeftStick,
            ["look"] = PadTarget.RightStick,
            ["a"] = PadTarget.A,
            ["b"] = PadTarget.B,
            ["x"] = PadTarget.X,
            ["y"] = PadTarget.Y,
            ["lb"] = PadTarget.LeftBumper,
            ["rb"] = PadTarget.RightBumper,
        }));
    }

    /// <summary>
    /// A GameCube pad with one stick instead of two, for Dolphin and friends. The targets follow
    /// Dolphin's standard Xbox profile: control stick on the left thumbstick, the analog L and R
    /// shoulders on the triggers, and Z on the right bumper.
    ///
    /// The C-stick is deliberately absent. Two full sticks plus a face diamond leaves a phone
    /// screen with nothing worth touching, and most GameCube and Wii games are playable without it.
    /// </summary>
    private static MappedSchema GameCube()
    {
        var schema = new Schema
        {
            Id = "gamecube",
            Name = "GameCube (single stick)",
            Orientation = SchemaOrientation.Landscape,
            IsPreset = true,
            Controls =
            [
                Stick("stick", ControlZone.Left, ControlSize.Large),
                // Four buttons in the right zone become the face diamond, as on the real pad.
                Button("a", "A", ControlZone.Right, ControlSize.Large),
                Button("b", "B", ControlZone.Right),
                Button("x", "X", ControlZone.Right),
                Button("y", "Y", ControlZone.Right),
                Button("r", "R", ControlZone.ShoulderRight, ControlSize.Large),
                Button("l", "L", ControlZone.ShoulderLeft, ControlSize.Large),
                Button("z", "Z", ControlZone.ShoulderRight),
                // Rarely reached mid-game, so both sit in the small top-centre row.
                Button("start", "Start", ControlZone.Aux, ControlSize.Small),
                Dpad("dpad", ControlZone.Aux, ControlSize.Small),
            ],
        };

        return new MappedSchema(schema, Map("gamecube", PadBackend.XInput, new()
        {
            ["stick"] = PadTarget.LeftStick,
            ["a"] = PadTarget.A,
            ["b"] = PadTarget.B,
            ["x"] = PadTarget.X,
            ["y"] = PadTarget.Y,
            ["l"] = PadTarget.LeftTrigger,
            ["r"] = PadTarget.RightTrigger,
            ["z"] = PadTarget.RightBumper,
            ["start"] = PadTarget.Start,
            ["dpad"] = PadTarget.Dpad,
        }));
    }

    /// <summary>
    /// The phone held like a Wii Remote: upright, top edge toward the TV, screen up. The layout
    /// is portrait for that reason — the motion feed is in the phone's own frame, so the phone
    /// simply <i>is</i> the remote's body and must be held like one.
    /// </summary>
    private static MappedSchema WiiRemote()
    {
        var schema = new Schema
        {
            Id = "wii-remote",
            Name = "Wii Remote",
            Orientation = SchemaOrientation.Portrait,
            IsPreset = true,
            Controls =
            [
                Button("a", "A", size: ControlSize.Large),
                // B is the trigger under a real remote; the top-left pill is the nearest thing.
                Button("b", "B", ControlZone.ShoulderLeft, ControlSize.Large),
                Dpad("dpad", ControlZone.Unspecified, ControlSize.Medium),
                Button("one", "1"),
                Button("two", "2"),
                Button("minus", "−", ControlZone.Aux, ControlSize.Small),
                Button("home", "Home", ControlZone.Aux, ControlSize.Small),
                Button("plus", "+", ControlZone.Aux, ControlSize.Small),
                // Gyro pointing drifts; players will need this every few minutes.
                Button("recenter", "Recenter", ControlZone.ShoulderRight),
                Motion("motion"),
            ],
        };

        return new MappedSchema(schema, Map("wii-remote", PadBackend.WiiRemote, WiiButtons()));
    }

    private static MappedSchema WiiRemoteNunchuk()
    {
        var schema = new Schema
        {
            Id = "wii-remote-nunchuk",
            Name = "Wii Remote + Nunchuk",
            Orientation = SchemaOrientation.Portrait,
            IsPreset = true,
            Controls =
            [
                Stick("stick", ControlZone.Left, ControlSize.Large),
                Button("a", "A", ControlZone.Right, ControlSize.Large),
                Button("b", "B", ControlZone.ShoulderRight, ControlSize.Large),
                Button("z", "Z", ControlZone.ShoulderLeft, ControlSize.Large),
                Button("c", "C", ControlZone.ShoulderLeft),
                Dpad("dpad", ControlZone.Unspecified, ControlSize.Small),
                Button("one", "1"),
                Button("two", "2"),
                Button("minus", "−", ControlZone.Aux, ControlSize.Small),
                Button("home", "Home", ControlZone.Aux, ControlSize.Small),
                Button("plus", "+", ControlZone.Aux, ControlSize.Small),
                Button("recenter", "Recenter", ControlZone.Aux, ControlSize.Small),
                Motion("motion"),
            ],
        };

        var targets = WiiButtons();
        targets["stick"] = WiiLayout.NunchukStick;
        targets["c"] = WiiLayout.NunchukC;
        targets["z"] = WiiLayout.NunchukZ;

        return new MappedSchema(schema, Map("wii-remote-nunchuk", PadBackend.WiiRemote, targets));
    }

    /// <summary>
    /// The remote held sideways, NES-style, for Mario Kart and the 2D Marios. Landscape is
    /// correct here: a sideways phone is a sideways remote, and the game expects exactly that.
    /// Dolphin's "Sideways Wii Remote" option does the button rotation.
    /// </summary>
    private static MappedSchema WiiRemoteSideways()
    {
        var schema = new Schema
        {
            Id = "wii-remote-sideways",
            Name = "Wii Remote (sideways)",
            Orientation = SchemaOrientation.Landscape,
            IsPreset = true,
            Controls =
            [
                Dpad("dpad", ControlZone.Left, ControlSize.Large),
                Button("two", "2", ControlZone.Right, ControlSize.Large),
                Button("one", "1", ControlZone.Right),
                Button("a", "A", ControlZone.ShoulderRight),
                Button("b", "B", ControlZone.ShoulderLeft),
                Button("minus", "−", ControlZone.Aux, ControlSize.Small),
                Button("home", "Home", ControlZone.Aux, ControlSize.Small),
                Button("plus", "+", ControlZone.Aux, ControlSize.Small),
                Motion("motion"),
            ],
        };

        var targets = WiiButtons();
        targets.Remove("recenter");

        return new MappedSchema(schema, Map("wii-remote-sideways", PadBackend.WiiRemote, targets));
    }

    private static Dictionary<string, PadTarget> WiiButtons() => new()
    {
        ["a"] = WiiLayout.A,
        ["b"] = WiiLayout.B,
        ["one"] = WiiLayout.One,
        ["two"] = WiiLayout.Two,
        ["plus"] = WiiLayout.Plus,
        ["minus"] = WiiLayout.Minus,
        ["home"] = WiiLayout.Home,
        ["dpad"] = WiiLayout.Dpad,
        ["recenter"] = WiiLayout.Recenter,
    };

    private static MappedSchema TwinStick()
    {
        var schema = new Schema
        {
            Id = "twin-stick",
            Name = "Twin-Stick Shooter",
            Orientation = SchemaOrientation.Landscape,
            IsPreset = true,
            Controls =
            [
                Stick("move", ControlZone.Left, ControlSize.Large),
                Stick("aim", ControlZone.Right, ControlSize.Large),
                Button("fire", "Fire", ControlZone.ShoulderRight, ControlSize.Large),
            ],
        };

        return new MappedSchema(schema, Map("twin-stick", PadBackend.XInput, new()
        {
            ["move"] = PadTarget.LeftStick,
            ["aim"] = PadTarget.RightStick,
            ["fire"] = PadTarget.RightTrigger,
        }));
    }

    private static MappedSchema Racing()
    {
        var schema = new Schema
        {
            Id = "racing",
            Name = "Racing",
            Orientation = SchemaOrientation.Landscape,
            IsPreset = true,
            Controls =
            [
                new SchemaControl
                {
                    Id = "steer",
                    Type = ControlType.Joystick,
                    Mode = ControlMode.XOnly,
                    Zone = ControlZone.Left,
                    Size = ControlSize.Large,
                    Label = "Steer",
                },
                Button("accelerate", "Go", ControlZone.ShoulderRight, ControlSize.Large),
                Button("brake", "Brake", ControlZone.ShoulderLeft, ControlSize.Large),
            ],
        };

        return new MappedSchema(schema, Map("racing", PadBackend.XInput, new()
        {
            ["steer"] = PadTarget.LeftStickX,
            ["accelerate"] = PadTarget.RightTrigger,
            ["brake"] = PadTarget.LeftTrigger,
        }));
    }

    private static MappedSchema Platformer()
    {
        var schema = new Schema
        {
            Id = "platformer",
            Name = "Platformer",
            IsPreset = true,
            Controls =
            [
                Dpad("move"),
                Button("jump", "Jump", ControlZone.Right, ControlSize.Large),
                Button("action", "Action", ControlZone.Right),
            ],
        };

        return new MappedSchema(schema, Map("platformer", PadBackend.XInput, new()
        {
            ["move"] = PadTarget.Dpad,
            ["jump"] = PadTarget.A,
            ["action"] = PadTarget.X,
        }));
    }

    private static MappedSchema Fighting()
    {
        var schema = new Schema
        {
            Id = "fighting",
            Name = "Fighting",
            Orientation = SchemaOrientation.Landscape,
            IsPreset = true,
            Controls =
            [
                Dpad("move"),
                Button("lp", "LP", ControlZone.Right),
                Button("mp", "MP", ControlZone.Right),
                Button("hp", "HP", ControlZone.Right),
                Button("lk", "LK", ControlZone.Right),
                Button("mk", "MK", ControlZone.ShoulderRight),
                Button("hk", "HK", ControlZone.ShoulderLeft),
            ],
        };

        return new MappedSchema(schema, Map("fighting", PadBackend.XInput, new()
        {
            ["move"] = PadTarget.Dpad,
            ["lp"] = PadTarget.X,
            ["mp"] = PadTarget.Y,
            ["hp"] = PadTarget.RightBumper,
            ["lk"] = PadTarget.A,
            ["mk"] = PadTarget.B,
            ["hk"] = PadTarget.RightTrigger,
        }));
    }

    private static MappedSchema Party()
    {
        var schema = new Schema
        {
            Id = "party-minimal",
            Name = "Party / Minimal",
            IsPreset = true,
            Controls =
            [
                Dpad("move"),
                Button("go", "GO", ControlZone.Right, ControlSize.Large),
            ],
        };

        return new MappedSchema(schema, Map("party-minimal", PadBackend.XInput, new()
        {
            ["move"] = PadTarget.Dpad,
            ["go"] = PadTarget.A,
        }));
    }

    private static SchemaControl Stick(string id, ControlZone zone, ControlSize size) => new()
    {
        Id = id,
        Type = ControlType.Joystick,
        Mode = ControlMode.Full,
        Zone = zone,
        Size = size,
    };

    private static SchemaControl Dpad(
        string id,
        ControlZone zone = ControlZone.Left,
        ControlSize size = ControlSize.Large) => new()
    {
        Id = id,
        Type = ControlType.Joystick,
        Mode = ControlMode.Dpad,
        Zone = zone,
        Size = size,
    };

    private static SchemaControl Button(
        string id,
        string label,
        ControlZone zone = ControlZone.Unspecified,
        ControlSize size = ControlSize.Unspecified) => new()
    {
        Id = id,
        Type = ControlType.Button,
        Label = label,
        Zone = zone,
        Size = size,
    };

    private static SchemaControl Motion(string id) => new()
    {
        Id = id,
        Type = ControlType.Motion,
    };

    private static Mapping Map(string schemaId, PadBackend backend, Dictionary<string, PadTarget> targets) => new()
    {
        SchemaId = schemaId,
        Backend = backend,
        Controls = targets.ToDictionary(
            pair => pair.Key,
            pair => new ControlMapping { Target = pair.Value },
            StringComparer.Ordinal),
    };
}
