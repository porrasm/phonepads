using MobileKbm.Core;
using Phonepads.Protocol;

namespace MobileKbm.Tests;

public class ControllerTests
{
    private readonly RecordingSink _sink = new();
    private readonly KbmController _kbm;
    private long _seq;

    public ControllerTests()
    {
        _kbm = new KbmController(_sink, KbmSchemas.All);
    }

    private void Press(double now, string control, bool pressed) =>
        _kbm.OnInput("p", ++_seq, new Dictionary<string, ControlValue> { [control] = ControlValue.Button(pressed) }, now);

    [Fact]
    public void A_button_holds_its_key_while_held()
    {
        Press(0, "enter", true);
        Press(50, "enter", false);

        Assert.Equal(["key+ Enter", "key- Enter"], _sink.Events);
    }

    [Fact]
    public void A_chord_goes_down_in_order_and_up_in_reverse()
    {
        Press(0, "keyboard", true);
        Press(50, "keyboard", false);

        Assert.Equal(
            ["key+ Win", "key+ Ctrl", "key+ O", "key- O", "key- Ctrl", "key- Win"],
            _sink.Events);
    }

    [Fact]
    public void Two_buttons_sharing_a_modifier_do_not_release_it_under_each_other()
    {
        _kbm.SetPlayer("p", "browser", connected: true);

        Press(0, "address", true);
        Press(10, "new-tab", true);
        Press(20, "address", false);

        Assert.DoesNotContain("key- Ctrl", _sink.Events);

        Press(30, "new-tab", false);
        Assert.Equal("key- Ctrl", _sink.Events[^1]);
    }

    [Fact]
    public void A_held_repeating_key_repeats_and_then_gives_up()
    {
        Press(0, "backspace", true);
        for (var t = 0; t <= 10_000; t += 15) _kbm.Tick(t);

        var downs = _sink.Events.Count(e => e == "key+ Backspace");
        // One press, then ~33 ms repeats from 450 ms until the 6 s cutoff.
        Assert.InRange(downs, 150, 180);
    }

    [Fact]
    public void Stale_frames_are_ignored()
    {
        _kbm.OnInput("p", 10, new Dictionary<string, ControlValue> { ["left"] = ControlValue.Button(true) }, 0);
        _kbm.OnInput("p", 9, new Dictionary<string, ControlValue> { ["left"] = ControlValue.Button(false) }, 1);

        Assert.Equal(["down Left"], _sink.Events);
    }

    [Fact]
    public void A_phone_that_drops_lets_go_of_what_it_held()
    {
        Press(0, "left", true);
        _kbm.SetPlayer("p", "mouse", connected: false);

        Assert.Equal(["down Left", "up Left"], _sink.Events);
    }

    [Fact]
    public void Pausing_lets_go_and_ignores_input()
    {
        Press(0, "enter", true);
        _kbm.Paused = true;
        Press(10, "esc", true);
        _kbm.OnText("p", "type", "hello");

        Assert.Equal(["key+ Enter", "key- Enter"], _sink.Events);
    }

    [Fact]
    public void Text_is_typed_as_sent()
    {
        _kbm.OnText("p", "type", "Helsinki");

        Assert.Equal(["text Helsinki"], _sink.Events);
    }

    [Fact]
    public void The_scroll_stick_scrolls_up_when_pushed_up()
    {
        _kbm.OnInput("p", ++_seq, new Dictionary<string, ControlValue> { ["scroll"] = ControlValue.Axes(0, -1) }, 0);
        _kbm.Tick(0);
        _kbm.Tick(100);

        Assert.Equal(["wheel 240"], _sink.Events);
    }

    [Fact]
    public void The_arrow_pad_holds_both_arrows_on_a_diagonal()
    {
        _kbm.SetPlayer("p", "keys", connected: true);

        _kbm.OnInput("p", ++_seq, new Dictionary<string, ControlValue> { ["arrows"] = ControlValue.DpadAt(DpadDirection.UpRight) }, 0);
        Assert.Equal(["key+ Right", "key+ Up"], _sink.Events);

        _kbm.OnInput("p", ++_seq, new Dictionary<string, ControlValue> { ["arrows"] = ControlValue.DpadAt(DpadDirection.Center) }, 50);
        Assert.Equal(["key+ Right", "key+ Up", "key- Right", "key- Up"], _sink.Events);
    }

    [Fact]
    public void A_value_of_the_wrong_shape_is_ignored()
    {
        // "left" is a button in this layout; a joystick-shaped value under that id is left alone.
        _kbm.OnInput("p", ++_seq, new Dictionary<string, ControlValue> { ["left"] = ControlValue.Axes(1, 1) }, 0);

        Assert.Empty(_sink.Events);
    }
}

public class GestureTests
{
    private readonly RecordingSink _sink = new();
    private readonly KbmController _kbm;
    private long _seq;

    public GestureTests()
    {
        _kbm = new KbmController(_sink, KbmSchemas.All);
        _kbm.SetPlayer("p", "touchpad", connected: true);
    }

    private void Touch(double now, params (double X, double Y)[] fingers)
    {
        var touches = fingers.Select((f, i) => new TouchPoint(i, f.X, f.Y)).ToList();
        _kbm.OnInput("p", ++_seq, new Dictionary<string, ControlValue> { ["surface"] = ControlValue.Fingers(touches) }, now);
    }

    [Fact]
    public void A_single_tap_does_nothing()
    {
        Touch(0, (0.5, 0.5));
        Touch(80);
        _kbm.Tick(1000);

        Assert.Empty(_sink.Events);
    }

    [Fact]
    public void A_double_tap_in_the_same_place_clicks()
    {
        Touch(0, (0.5, 0.5));
        Touch(80);
        Touch(200, (0.51, 0.5));
        Touch(280);

        Assert.Equal(["down Left", "up Left"], _sink.Events);
    }

    [Fact]
    public void Two_taps_far_apart_do_not_click()
    {
        Touch(0, (0.2, 0.2));
        Touch(80);
        Touch(200, (0.8, 0.8));
        Touch(280);

        Assert.Empty(_sink.Events);
    }

    [Fact]
    public void Two_taps_too_slow_do_not_click()
    {
        Touch(0, (0.5, 0.5));
        Touch(80);
        Touch(800, (0.5, 0.5));
        Touch(880);

        Assert.Empty(_sink.Events);
    }

    [Fact]
    public void A_two_finger_tap_right_clicks()
    {
        Touch(0, (0.4, 0.5), (0.6, 0.5));
        Touch(90);

        Assert.Equal(["down Right", "up Right"], _sink.Events);
    }

    [Fact]
    public void Sliding_one_finger_moves_the_pointer_without_clicking()
    {
        Touch(0, (0.5, 0.5));
        Touch(50, (0.5, 0.55));
        Touch(100);

        var move = Assert.Single(_sink.Events);
        Assert.StartsWith("move 0,", move);
        Assert.True(int.Parse(move.Split(',')[1]) > 0, "Moving the finger down moves the pointer down.");
    }

    [Fact]
    public void Tap_then_touch_and_slide_drags()
    {
        Touch(0, (0.5, 0.5));
        Touch(80);
        Touch(200, (0.5, 0.5));
        Touch(260, (0.5, 0.6));
        Touch(400);

        Assert.Equal("down Left", _sink.Events[0]);
        Assert.StartsWith("move", _sink.Events[1]);
        Assert.Equal("up Left", _sink.Events[^1]);
    }

    [Fact]
    public void Tap_then_touch_and_hold_drags()
    {
        Touch(0, (0.5, 0.5));
        Touch(80);
        Touch(200, (0.5, 0.5));
        _kbm.Tick(600);

        Assert.Equal(["down Left"], _sink.Events);

        Touch(700);
        Assert.Equal(["down Left", "up Left"], _sink.Events);
    }

    [Fact]
    public void Two_fingers_scroll_with_the_content_following_them()
    {
        Touch(0, (0.4, 0.3), (0.6, 0.3));
        Touch(50, (0.4, 0.4), (0.6, 0.4));
        Touch(100);

        // Fingers down 0.1 of the surface: content follows them down, so the wheel turns up.
        Assert.Equal(["wheel 180"], _sink.Events);
    }

    [Fact]
    public void Switching_layout_mid_drag_lets_go()
    {
        Touch(0, (0.5, 0.5));
        Touch(80);
        Touch(200, (0.5, 0.5));
        Touch(260, (0.5, 0.6));
        _kbm.SetPlayer("p", "mouse", connected: true);

        Assert.Equal("up Left", _sink.Events[^1]);
    }
}
