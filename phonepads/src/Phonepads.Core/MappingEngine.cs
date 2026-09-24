using Phonepads.Protocol;

namespace Phonepads.Core;

/// <summary>
/// Translates an input frame into a virtual pad state. This is where the two coordinate
/// conventions meet: the phone sends y positive <b>downwards</b>, gamepads expect y positive
/// <b>upwards</b>, so every vertical axis is negated on the way through.
/// </summary>
public static class MappingEngine
{
    /// <summary>Deflection past which an analogue control counts as a dpad direction press.</summary>
    private const double DpadThreshold = 0.5;

    public static PadState Apply(
        Schema schema,
        Mapping mapping,
        IReadOnlyDictionary<string, ControlValue> controls)
    {
        double leftX = 0, leftY = 0, rightX = 0, rightY = 0;
        double leftTrigger = 0, rightTrigger = 0;
        var buttons = PadButtons.None;

        foreach (var control in schema.Controls)
        {
            if (mapping.For(control.Id) is not { Target: not PadTarget.None } assignment) continue;
            if (!controls.TryGetValue(control.Id, out var value)) continue;

            var tuning = assignment.Tuning;

            switch (value.Kind)
            {
                case ControlValueKind.Button:
                    if (!value.Pressed) break;
                    if (assignment.Target == PadTarget.LeftTrigger) leftTrigger = Stronger(leftTrigger, 1);
                    else if (assignment.Target == PadTarget.RightTrigger) rightTrigger = Stronger(rightTrigger, 1);
                    else buttons |= ButtonFlag(assignment.Target);
                    break;

                case ControlValueKind.Dpad:
                {
                    var (dx, dy) = ControlValue.DpadToVector(value.Dpad);
                    ApplyAxes(assignment.Target, control, tuning, dx, dy,
                        ref leftX, ref leftY, ref rightX, ref rightY, ref buttons);
                    break;
                }

                case ControlValueKind.Axes:
                    ApplyAxes(assignment.Target, control, tuning, value.X, value.Y,
                        ref leftX, ref leftY, ref rightX, ref rightY, ref buttons);
                    break;
            }
        }

        return new PadState
        {
            LeftStickX = PadState.ToAxis(leftX),
            LeftStickY = PadState.ToAxis(leftY),
            RightStickX = PadState.ToAxis(rightX),
            RightStickY = PadState.ToAxis(rightY),
            LeftTrigger = PadState.ToTrigger(leftTrigger),
            RightTrigger = PadState.ToTrigger(rightTrigger),
            Buttons = buttons,
        };
    }

    private static void ApplyAxes(
        PadTarget target,
        SchemaControl control,
        AxisTuning tuning,
        double rawX,
        double rawY,
        ref double leftX,
        ref double leftY,
        ref double rightX,
        ref double rightY,
        ref PadButtons buttons)
    {
        var x = Condition(rawX, tuning.Deadzone, tuning.Sensitivity, tuning.InvertX);
        // Flip y: the phone's screen coordinates run downwards, a thumbstick's run upwards.
        var y = -Condition(rawY, tuning.Deadzone, tuning.Sensitivity, tuning.InvertY);

        switch (target)
        {
            case PadTarget.LeftStick:
                leftX = Stronger(leftX, x);
                leftY = Stronger(leftY, y);
                break;

            case PadTarget.RightStick:
                rightX = Stronger(rightX, x);
                rightY = Stronger(rightY, y);
                break;

            case PadTarget.LeftStickX:
                leftX = Stronger(leftX, PrimaryAxis(control, x, y));
                break;

            case PadTarget.LeftStickY:
                leftY = Stronger(leftY, PrimaryAxis(control, x, y));
                break;

            case PadTarget.RightStickX:
                rightX = Stronger(rightX, PrimaryAxis(control, x, y));
                break;

            case PadTarget.RightStickY:
                rightY = Stronger(rightY, PrimaryAxis(control, x, y));
                break;

            case PadTarget.Dpad:
                if (x <= -DpadThreshold) buttons |= PadButtons.DpadLeft;
                if (x >= DpadThreshold) buttons |= PadButtons.DpadRight;
                if (y >= DpadThreshold) buttons |= PadButtons.DpadUp;
                if (y <= -DpadThreshold) buttons |= PadButtons.DpadDown;
                break;

            default:
                // An analogue source on a button target presses it past the threshold.
                if (Math.Abs(x) >= DpadThreshold || Math.Abs(y) >= DpadThreshold)
                    buttons |= ButtonFlag(target);
                break;
        }
    }

    /// <summary>
    /// Picks which incoming axis feeds a single-axis target. A control that only reports one
    /// axis drives the target with that axis whichever way round the target is.
    /// </summary>
    private static double PrimaryAxis(SchemaControl control, double x, double y) => control.Mode switch
    {
        ControlMode.XOnly => x,
        ControlMode.YOnly => y,
        _ => Math.Abs(x) >= Math.Abs(y) ? x : y,
    };

    /// <summary>
    /// Applies deadzone, sensitivity and inversion. Travel outside the deadzone is rescaled
    /// so the control still reaches full deflection.
    /// </summary>
    private static double Condition(double value, double deadzone, double sensitivity, bool invert)
    {
        var magnitude = Math.Abs(value);
        if (deadzone > 0)
        {
            if (magnitude <= deadzone) return 0;
            magnitude = (magnitude - deadzone) / (1 - deadzone);
        }

        var conditioned = Math.Sign(value) * magnitude * sensitivity;
        if (invert) conditioned = -conditioned;
        return Math.Clamp(conditioned, -1d, 1d);
    }

    /// <summary>
    /// Combines two controls pointing at the same axis by keeping the larger deflection, so
    /// a centred control never cancels an active one.
    /// </summary>
    private static double Stronger(double current, double candidate) =>
        Math.Abs(candidate) > Math.Abs(current) ? candidate : current;

    private static PadButtons ButtonFlag(PadTarget target) => target switch
    {
        PadTarget.A => PadButtons.A,
        PadTarget.B => PadButtons.B,
        PadTarget.X => PadButtons.X,
        PadTarget.Y => PadButtons.Y,
        PadTarget.LeftBumper => PadButtons.LeftBumper,
        PadTarget.RightBumper => PadButtons.RightBumper,
        PadTarget.Back => PadButtons.Back,
        PadTarget.Start => PadButtons.Start,
        PadTarget.LeftThumbClick => PadButtons.LeftThumb,
        PadTarget.RightThumbClick => PadButtons.RightThumb,
        PadTarget.DpadUp => PadButtons.DpadUp,
        PadTarget.DpadDown => PadButtons.DpadDown,
        PadTarget.DpadLeft => PadButtons.DpadLeft,
        PadTarget.DpadRight => PadButtons.DpadRight,
        PadTarget.Guide => PadButtons.Guide,
        _ => PadButtons.None,
    };
}
