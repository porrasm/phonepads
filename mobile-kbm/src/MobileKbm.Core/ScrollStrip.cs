using Phonepads.Protocol;

namespace MobileKbm.Core;

/// <summary>
/// A scroll strip: one finger dragged along it turns the mouse wheel, the content following
/// the finger like on a phone. Only the first finger counts; a second one on the strip is ignored.
/// </summary>
internal sealed class ScrollStrip(HeldInput output)
{
    /// <summary>Wheel units per full strip length of travel (120 is one notch).</summary>
    internal const double WheelPerLength = 1200;

    private int? _finger;
    private double _lastY;
    private double _wheelRest;

    public void Update(IReadOnlyList<TouchPoint> touches)
    {
        TouchPoint? current = null;
        foreach (var touch in touches)
        {
            if (touch.Id == _finger) current = touch;
        }

        if (current is not { } finger)
        {
            // Nothing down, or the finger we followed lifted: start over from whichever is down now.
            Reset();
            if (touches.Count == 0) return;
            _finger = touches[0].Id;
            _lastY = touches[0].Y;
            return;
        }

        var dy = finger.Y - _lastY;
        _lastY = finger.Y;
        // Re-landed between frames (ids are reused): not a drag.
        if (Math.Abs(dy) > TouchpadGestures.JumpLimit) return;

        // Finger down reveals what is above: wheel up, which is positive.
        _wheelRest += dy * WheelPerLength;
        var chunks = (int)(_wheelRest / TouchpadGestures.WheelChunk);
        if (chunks == 0) return;

        var delta = chunks * TouchpadGestures.WheelChunk;
        _wheelRest -= delta;
        output.Sink.Wheel(delta, horizontal: false);
    }

    public void Reset()
    {
        _finger = null;
        _wheelRest = 0;
    }
}
