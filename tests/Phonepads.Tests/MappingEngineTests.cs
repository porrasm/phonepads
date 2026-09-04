using Phonepads.Core;
using Phonepads.Protocol;

namespace Phonepads.Tests;

public class MappingEngineTests
{
    private static Schema SchemaWith(params SchemaControl[] controls) => new()
    {
        Id = "test-schema",
        Name = "Test",
        Controls = controls,
    };

    private static Mapping MappingOf(params (string Id, PadTarget Target)[] entries) => new()
    {
        SchemaId = "test-schema",
        Controls = entries.ToDictionary(
            e => e.Id,
            e => new ControlMapping { Target = e.Target },
            StringComparer.Ordinal),
    };

    private static SchemaControl Stick(string id, ControlMode mode = ControlMode.Full) => new()
    {
        Id = id,
        Type = ControlType.Joystick,
        Mode = mode,
    };

    private static SchemaControl Button(string id) => new() { Id = id, Type = ControlType.Button };

    [Fact]
    public void Flips_y_because_the_phone_and_the_pad_disagree_about_down()
    {
        var schema = SchemaWith(Stick("move"));
        var mapping = MappingOf(("move", PadTarget.LeftStick));

        // The phone reports y positive downwards, so "fully down" is +1.
        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["move"] = ControlValue.Axes(0, 1),
        });

        Assert.True(state.LeftStickY < 0, "pushing down on the phone must push the thumbstick down");
        Assert.Equal(short.MinValue, state.LeftStickY);
    }

    [Fact]
    public void Passes_x_through_unchanged()
    {
        var schema = SchemaWith(Stick("move"));
        var mapping = MappingOf(("move", PadTarget.LeftStick));

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["move"] = ControlValue.Axes(1, 0),
        });

        Assert.Equal(short.MaxValue, state.LeftStickX);
        Assert.Equal(0, state.LeftStickY);
    }

    [Fact]
    public void Unmapped_controls_do_nothing()
    {
        var schema = SchemaWith(Stick("move"), Button("fire"));
        var mapping = MappingOf(("move", PadTarget.LeftStick)); // "fire" deliberately unmapped

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["move"] = ControlValue.Axes(0, 0),
            ["fire"] = ControlValue.Button(true),
        });

        Assert.Equal(PadButtons.None, state.Buttons);
    }

    [Fact]
    public void Buttons_set_their_flag()
    {
        var schema = SchemaWith(Button("jump"));
        var mapping = MappingOf(("jump", PadTarget.A));

        var pressed = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["jump"] = ControlValue.Button(true),
        });
        var released = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["jump"] = ControlValue.Button(false),
        });

        Assert.True(pressed.IsPressed(PadButtons.A));
        Assert.False(released.IsPressed(PadButtons.A));
    }

    [Fact]
    public void A_button_on_a_trigger_presses_it_fully()
    {
        var schema = SchemaWith(Button("fire"));
        var mapping = MappingOf(("fire", PadTarget.RightTrigger));

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["fire"] = ControlValue.Button(true),
        });

        Assert.Equal(255, state.RightTrigger);
    }

    [Theory]
    [InlineData(DpadDirection.Up, PadButtons.DpadUp)]
    [InlineData(DpadDirection.Down, PadButtons.DpadDown)]
    [InlineData(DpadDirection.Left, PadButtons.DpadLeft)]
    [InlineData(DpadDirection.Right, PadButtons.DpadRight)]
    public void Dpad_directions_reach_the_matching_pad_button(DpadDirection direction, PadButtons expected)
    {
        var schema = SchemaWith(Stick("hat", ControlMode.Dpad));
        var mapping = MappingOf(("hat", PadTarget.Dpad));

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["hat"] = ControlValue.DpadAt(direction),
        });

        Assert.True(state.IsPressed(expected));
    }

    [Fact]
    public void Diagonal_dpad_presses_both_directions()
    {
        var schema = SchemaWith(Stick("hat", ControlMode.Dpad));
        var mapping = MappingOf(("hat", PadTarget.Dpad));

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["hat"] = ControlValue.DpadAt(DpadDirection.UpRight),
        });

        Assert.True(state.IsPressed(PadButtons.DpadUp));
        Assert.True(state.IsPressed(PadButtons.DpadRight));
        Assert.False(state.IsPressed(PadButtons.DpadDown));
    }

    [Fact]
    public void A_centred_dpad_releases_everything()
    {
        var schema = SchemaWith(Stick("hat", ControlMode.Dpad));
        var mapping = MappingOf(("hat", PadTarget.Dpad));

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["hat"] = ControlValue.DpadAt(DpadDirection.Center),
        });

        Assert.Equal(PadButtons.None, state.Buttons);
    }

    [Fact]
    public void A_dpad_can_drive_a_thumbstick()
    {
        var schema = SchemaWith(Stick("hat", ControlMode.Dpad));
        var mapping = MappingOf(("hat", PadTarget.LeftStick));

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["hat"] = ControlValue.DpadAt(DpadDirection.Up),
        });

        // "Up" on screen is -1 in phone coordinates, which becomes +1 on the stick.
        Assert.Equal(short.MaxValue, state.LeftStickY);
    }

    [Fact]
    public void Deadzone_silences_small_movement_and_rescales_the_rest()
    {
        var schema = SchemaWith(Stick("move"));
        var mapping = new Mapping
        {
            SchemaId = "test-schema",
            Controls = new Dictionary<string, ControlMapping>
            {
                ["move"] = new()
                {
                    Target = PadTarget.LeftStick,
                    Tuning = new AxisTuning { Deadzone = 0.5 },
                },
            },
        };

        var inside = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["move"] = ControlValue.Axes(0.4, 0),
        });
        var halfway = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["move"] = ControlValue.Axes(0.75, 0),
        });
        var full = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["move"] = ControlValue.Axes(1, 0),
        });

        Assert.Equal(0, inside.LeftStickX);
        // 0.75 sits halfway through the live part of the travel.
        Assert.InRange(halfway.LeftStickX / 32767d, 0.49, 0.51);
        Assert.Equal(short.MaxValue, full.LeftStickX);
    }

    [Fact]
    public void Invert_flips_the_axis()
    {
        var schema = SchemaWith(Stick("look"));
        var mapping = new Mapping
        {
            SchemaId = "test-schema",
            Controls = new Dictionary<string, ControlMapping>
            {
                ["look"] = new()
                {
                    Target = PadTarget.RightStick,
                    Tuning = new AxisTuning { InvertY = true },
                },
            },
        };

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["look"] = ControlValue.Axes(0, 1),
        });

        // Without inversion this would be fully negative; inverted it is fully positive.
        Assert.Equal(short.MaxValue, state.RightStickY);
    }

    [Fact]
    public void Sensitivity_scales_and_still_clamps()
    {
        var schema = SchemaWith(Stick("move"));
        var mapping = new Mapping
        {
            SchemaId = "test-schema",
            Controls = new Dictionary<string, ControlMapping>
            {
                ["move"] = new()
                {
                    Target = PadTarget.LeftStick,
                    Tuning = new AxisTuning { Sensitivity = 2.0 },
                },
            },
        };

        var half = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["move"] = ControlValue.Axes(0.25, 0),
        });
        var over = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["move"] = ControlValue.Axes(0.9, 0),
        });

        Assert.InRange(half.LeftStickX / 32767d, 0.49, 0.51);
        Assert.Equal(short.MaxValue, over.LeftStickX);
    }

    [Fact]
    public void A_single_axis_control_drives_a_single_axis_target()
    {
        var schema = SchemaWith(Stick("steer", ControlMode.XOnly));
        var mapping = MappingOf(("steer", PadTarget.LeftStickX));

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["steer"] = ControlValue.Axes(-1, 0),
        });

        Assert.Equal(short.MinValue, state.LeftStickX);
        Assert.Equal(0, state.LeftStickY);
    }

    [Fact]
    public void Two_controls_on_one_target_keep_the_larger_deflection()
    {
        var schema = SchemaWith(Stick("stick"), Stick("hat", ControlMode.Dpad));
        var mapping = MappingOf(("stick", PadTarget.LeftStick), ("hat", PadTarget.LeftStick));

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["stick"] = ControlValue.Axes(0.2, 0),
            ["hat"] = ControlValue.DpadAt(DpadDirection.Right), // full deflection
        });

        Assert.Equal(short.MaxValue, state.LeftStickX);
    }

    [Fact]
    public void A_missing_control_in_the_frame_is_ignored()
    {
        var schema = SchemaWith(Stick("move"), Button("fire"));
        var mapping = MappingOf(("move", PadTarget.LeftStick), ("fire", PadTarget.A));

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
        {
            ["move"] = ControlValue.Axes(1, 0),
        });

        Assert.Equal(short.MaxValue, state.LeftStickX);
        Assert.False(state.IsPressed(PadButtons.A));
    }

    [Fact]
    public void An_empty_frame_produces_a_neutral_pad()
    {
        var schema = SchemaWith(Stick("move"));
        var mapping = MappingOf(("move", PadTarget.LeftStick));

        var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>());

        Assert.Equal(PadState.Neutral, state);
    }
}
