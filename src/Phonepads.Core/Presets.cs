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

        return new MappedSchema(schema, Map("generic-gamepad", new()
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

        return new MappedSchema(schema, Map("twin-stick", new()
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

        return new MappedSchema(schema, Map("racing", new()
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

        return new MappedSchema(schema, Map("platformer", new()
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

        return new MappedSchema(schema, Map("fighting", new()
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

        return new MappedSchema(schema, Map("party-minimal", new()
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

    private static SchemaControl Dpad(string id) => new()
    {
        Id = id,
        Type = ControlType.Joystick,
        Mode = ControlMode.Dpad,
        Zone = ControlZone.Left,
        Size = ControlSize.Large,
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

    private static Mapping Map(string schemaId, Dictionary<string, PadTarget> targets) => new()
    {
        SchemaId = schemaId,
        Controls = targets.ToDictionary(
            pair => pair.Key,
            pair => new ControlMapping { Target = pair.Value },
            StringComparer.Ordinal),
    };
}
