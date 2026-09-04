using Phonepads.Core;

namespace Phonepads.Tests;

public class SchemaValidationTests
{
    private static Schema WithControls(params SchemaControl[] controls) => new()
    {
        Id = "test-schema",
        Name = "Test",
        Controls = controls,
    };

    private static SchemaControl Button(string id) => new() { Id = id, Type = ControlType.Button };

    [Fact]
    public void A_reasonable_schema_has_no_problems()
    {
        Assert.Empty(WithControls(Button("fire")).Validate());
    }

    [Fact]
    public void A_schema_with_no_controls_is_rejected()
    {
        Assert.NotEmpty(WithControls().Validate());
    }

    [Fact]
    public void More_than_sixteen_controls_is_rejected()
    {
        var controls = Enumerable.Range(0, 17).Select(i => Button("b" + i)).ToArray();

        Assert.Contains(WithControls(controls).Validate(), p => p.Contains("1 to 16"));
    }

    [Fact]
    public void Duplicate_control_ids_are_rejected()
    {
        var problems = WithControls(Button("fire"), Button("fire")).Validate();

        Assert.Contains(problems, p => p.Contains("more than once"));
    }

    [Theory]
    [InlineData("Fire")]
    [InlineData("fire_button")]
    [InlineData("9lives")]
    [InlineData("trailing-")]
    public void Control_ids_must_be_kebab_case(string id)
    {
        var problems = WithControls(Button(id)).Validate();

        Assert.Contains(problems, p => p.Contains("kebab-case"));
    }

    [Theory]
    [InlineData("fire")]
    [InlineData("left-bumper")]
    [InlineData("b2")]
    public void Valid_kebab_case_ids_pass(string id)
    {
        Assert.Empty(WithControls(Button(id)).Validate());
    }
}

public class SchemaDtoTests
{
    [Fact]
    public void A_dpad_joystick_serialises_with_its_mode()
    {
        var dto = new SchemaControl
        {
            Id = "hat",
            Type = ControlType.Joystick,
            Mode = ControlMode.Dpad,
            Zone = ControlZone.ShoulderLeft,
            Size = ControlSize.Large,
        }.ToDto();

        Assert.Equal("joystick", dto.Type);
        Assert.Equal("dpad", dto.Mode);
        Assert.Equal("shoulder-left", dto.Zone);
        Assert.Equal("large", dto.Size);
    }

    [Fact]
    public void A_button_carries_no_mode_or_range()
    {
        var dto = new SchemaControl { Id = "fire", Type = ControlType.Button, Label = "Fire" }.ToDto();

        Assert.Equal("button", dto.Type);
        Assert.Null(dto.Mode);
        Assert.Null(dto.Range);
        Assert.Equal("Fire", dto.Label);
    }

    [Fact]
    public void A_gyro_keeps_its_range()
    {
        var dto = new SchemaControl { Id = "lean", Type = ControlType.Gyro, Range = 45 }.ToDto();

        Assert.Equal("gyro", dto.Type);
        Assert.Equal(45, dto.Range);
    }

    [Fact]
    public void Control_order_is_preserved_because_it_drives_the_phone_layout()
    {
        var schema = new Schema
        {
            Id = "ordered",
            Name = "Ordered",
            Controls =
            [
                new SchemaControl { Id = "first", Type = ControlType.Button },
                new SchemaControl { Id = "second", Type = ControlType.Button },
            ],
        };

        var dto = schema.ToDto();

        Assert.Equal("first", dto.Controls[0].Id);
        Assert.Equal("second", dto.Controls[1].Id);
    }
}

public class PresetTests
{
    [Fact]
    public void Every_preset_is_valid()
    {
        foreach (var preset in Presets.All)
            Assert.Empty(preset.Schema.Validate());
    }

    [Fact]
    public void Every_preset_maps_every_control_it_declares()
    {
        foreach (var preset in Presets.All)
        {
            var (unmapped, _) = preset.Mapping.Review(preset.Schema);
            Assert.True(unmapped.Count == 0,
                $"'{preset.Schema.Name}' leaves these controls unmapped: {string.Join(", ", unmapped)}");
        }
    }

    [Fact]
    public void No_preset_maps_two_controls_onto_one_target()
    {
        foreach (var preset in Presets.All)
        {
            var (_, shared) = preset.Mapping.Review(preset.Schema);
            Assert.True(shared.Count == 0,
                $"'{preset.Schema.Name}' doubles up on: {string.Join(", ", shared)}");
        }
    }

    [Fact]
    public void Preset_mappings_only_reference_controls_that_exist()
    {
        foreach (var preset in Presets.All)
        {
            var ids = preset.Schema.Controls.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var mapped in preset.Mapping.Controls.Keys)
                Assert.Contains(mapped, ids);
        }
    }

    [Fact]
    public void Preset_ids_are_unique()
    {
        var ids = Presets.All.Select(p => p.Schema.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void At_least_one_preset_works_without_motion_sensors()
    {
        Assert.Contains(Presets.All, p => !p.Schema.RequiresGyro);
    }

    [Fact]
    public void Presets_are_marked_read_only()
    {
        Assert.All(Presets.All, p => Assert.True(p.Schema.IsPreset));
    }
}

public class MappingReviewTests
{
    [Fact]
    public void Reports_unmapped_controls_and_shared_targets()
    {
        var schema = new Schema
        {
            Id = "review",
            Name = "Review",
            Controls =
            [
                new SchemaControl { Id = "a", Type = ControlType.Button },
                new SchemaControl { Id = "b", Type = ControlType.Button },
                new SchemaControl { Id = "c", Type = ControlType.Button },
            ],
        };

        var mapping = new Mapping
        {
            SchemaId = "review",
            Controls = new Dictionary<string, ControlMapping>
            {
                ["a"] = new() { Target = PadTarget.A },
                ["b"] = new() { Target = PadTarget.A }, // same target as "a"
                // "c" left unmapped
            },
        };

        var (unmapped, shared) = mapping.Review(schema);

        Assert.Equal(["c"], unmapped);
        Assert.Equal([PadTarget.A], shared);
    }
}

public class PadStateTests
{
    [Fact]
    public void Full_deflection_reaches_the_ends_of_the_axis_range()
    {
        Assert.Equal(short.MaxValue, PadState.ToAxis(1));
        Assert.Equal(short.MinValue, PadState.ToAxis(-1));
        Assert.Equal(0, PadState.ToAxis(0));
    }

    [Fact]
    public void Axis_values_are_clamped()
    {
        Assert.Equal(short.MaxValue, PadState.ToAxis(4));
        Assert.Equal(short.MinValue, PadState.ToAxis(-4));
    }

    [Fact]
    public void Triggers_span_the_byte_range()
    {
        Assert.Equal(0, PadState.ToTrigger(0));
        Assert.Equal(255, PadState.ToTrigger(1));
        Assert.Equal(128, PadState.ToTrigger(0.5));
        Assert.Equal(0, PadState.ToTrigger(-1));
        Assert.Equal(255, PadState.ToTrigger(2));
    }

    [Fact]
    public void A_neutral_pad_holds_nothing_down()
    {
        var neutral = PadState.Neutral;

        Assert.Equal(0, neutral.LeftStickX);
        Assert.Equal(0, neutral.RightStickY);
        Assert.Equal(0, neutral.LeftTrigger);
        Assert.Equal(PadButtons.None, neutral.Buttons);
    }
}
