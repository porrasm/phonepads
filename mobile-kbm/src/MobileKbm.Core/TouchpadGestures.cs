using Phonepads.Protocol;

namespace MobileKbm.Core;

/// <summary>
/// Reads one touch surface like a laptop touchpad. The phone only reports where fingers are;
/// everything else is decided here:
/// <list type="bullet">
/// <item>one finger slides the pointer, faster the faster it moves;</item>
/// <item>two quick taps in the same place click (a lone tap does nothing, so lifting and
/// re-placing a thumb mid-move never clicks by accident);</item>
/// <item>tap, then touch again and slide (or hold) to drag;</item>
/// <item>two fingers scroll, like on a phone — the content follows the fingers;</item>
/// <item>a two-finger tap right-clicks, a three-finger tap middle-clicks.</item>
/// </list>
/// Distances are in "surface heights": x is multiplied by the aspect, so a step across and a
/// step down of the same length move the pointer equally far.
/// </summary>
internal sealed class TouchpadGestures(double aspect, HeldInput output)
{
    /// <summary>Longest touch that still counts as a tap.</summary>
    internal const double TapMaxMs = 250;

    /// <summary>Furthest a finger may wander and still be tapping rather than moving.</summary>
    internal const double TapSlop = 0.04;

    /// <summary>Longest gap from the first tap's lift to the second touch.</summary>
    internal const double DoubleTapGapMs = 350;

    /// <summary>How close the second tap must land to the first to make a double tap.</summary>
    internal const double SameSpot = 0.12;

    /// <summary>
    /// A finger that moves further than this between two frames lifted and landed again in
    /// between (finger ids are reused, and frames can merge); it must not fling the pointer.
    /// </summary>
    internal const double JumpLimit = 0.3;

    /// <summary>Pointer pixels per surface height at slow speed, as a share of the screen height.</summary>
    internal const double PointerScreens = 0.7;

    /// <summary>Extra gain at speed: up to (1 + this) times the slow rate.</summary>
    internal const double PointerAcceleration = 1.5;

    /// <summary>Wheel units per surface height of two-finger travel (120 is one notch).</summary>
    internal const double WheelPerHeight = 1800;

    /// <summary>Two-finger travel before a scroll commits to vertical or horizontal.</summary>
    internal const double AxisLockDistance = 0.02;

    /// <summary>Smallest wheel step sent: a quarter notch, fine for smooth scrolling and legacy apps alike.</summary>
    internal const int WheelChunk = 30;

    private sealed class Finger(double x, double y, double now)
    {
        public double X = x;
        public double Y = y;
        public readonly double DownX = x;
        public readonly double DownY = y;
        public double LastMove = now;
    }

    private enum ScrollAxis { Undecided, Vertical, Horizontal }

    private readonly Dictionary<int, Finger> _fingers = [];

    // One gesture runs from the first finger down until the last one lifts.
    private bool _inGesture;
    private double _gestureStart;
    private double _startX;
    private double _startY;
    private int _maxFingers;
    private bool _moved;
    private bool _secondTap;
    private bool _dragging;

    /// <summary>A lone tap waiting to see whether a second one follows.</summary>
    private (double X, double Y, double At)? _pendingTap;

    private ScrollAxis _axis;
    private double _scrollX;
    private double _scrollY;
    private double _pointerRestX;
    private double _pointerRestY;
    private double _wheelRest;

    /// <summary>Applies the fingers of one input frame, received at <paramref name="now"/> (ms).</summary>
    public void Update(IReadOnlyList<TouchPoint> touches, double now)
    {
        double sumDx = 0, sumDy = 0, lastDt = 0;
        var persisting = 0;
        var seen = new HashSet<int>();

        foreach (var touch in touches)
        {
            if (!seen.Add(touch.Id)) continue;
            var x = touch.X * aspect;
            var y = touch.Y;

            if (!_fingers.TryGetValue(touch.Id, out var finger))
            {
                if (!_inGesture) BeginGesture(x, y, now);
                _fingers[touch.Id] = new Finger(x, y, now);
                continue;
            }

            var dx = x - finger.X;
            var dy = y - finger.Y;
            if (Math.Abs(dx) > JumpLimit || Math.Abs(dy) > JumpLimit)
            {
                // Re-landed between frames: start this finger over from where it is now.
                _fingers[touch.Id] = new Finger(x, y, now);
                _moved = true;
                continue;
            }

            persisting++;
            if (dx == 0 && dy == 0) continue;

            lastDt = now - finger.LastMove;
            finger.X = x;
            finger.Y = y;
            finger.LastMove = now;
            sumDx += dx;
            sumDy += dy;

            if (Distance(x, y, finger.DownX, finger.DownY) > TapSlop) _moved = true;
        }

        foreach (var id in _fingers.Keys.Where(id => !seen.Contains(id)).ToList())
            _fingers.Remove(id);

        _maxFingers = Math.Max(_maxFingers, _fingers.Count);

        if (sumDx != 0 || sumDy != 0)
        {
            if (_maxFingers == 1 && _fingers.Count == 1)
            {
                // The second half of a double tap that starts sliding is a drag.
                if (_secondTap && !_dragging && _moved) StartDrag();
                MovePointer(sumDx, sumDy, lastDt);
            }
            else if (_fingers.Count == 2 && persisting == 2)
            {
                Scroll(sumDx / 2, sumDy / 2);
            }

            // Three or more fingers, or the one left over after a scroll: nothing, on purpose.
        }

        if (_inGesture && _fingers.Count == 0) EndGesture(now);
    }

    /// <summary>Time passing with no frame: a second tap held still turns into a drag.</summary>
    public void Tick(double now)
    {
        if (_inGesture && _secondTap && !_dragging && _maxFingers == 1 && _fingers.Count == 1
            && now - _gestureStart > TapMaxMs)
        {
            StartDrag();
        }
    }

    /// <summary>Forgets every finger and lets go of a drag in progress.</summary>
    public void Reset()
    {
        if (_dragging) output.Release(MouseButton.Left);
        _dragging = false;
        _inGesture = false;
        _pendingTap = null;
        _fingers.Clear();
    }

    private void BeginGesture(double x, double y, double now)
    {
        _inGesture = true;
        _gestureStart = now;
        _startX = x;
        _startY = y;
        _maxFingers = 0;
        _moved = false;
        _dragging = false;
        _axis = ScrollAxis.Undecided;
        _scrollX = _scrollY = 0;

        _secondTap = _pendingTap is { } tap
                     && now - tap.At <= DoubleTapGapMs
                     && Distance(x, y, tap.X, tap.Y) <= SameSpot;
        _pendingTap = null;
    }

    private void EndGesture(double now)
    {
        _inGesture = false;

        if (_dragging)
        {
            // Includes a second tap held a little too long: press, release, same place — a click.
            output.Release(MouseButton.Left);
            _dragging = false;
            return;
        }

        var tapped = !_moved && now - _gestureStart <= TapMaxMs;
        if (!tapped) return;

        switch (_maxFingers)
        {
            case 1 when _secondTap:
                output.Click(MouseButton.Left);
                break;
            case 1:
                _pendingTap = (_startX, _startY, now);
                break;
            case 2:
                output.Click(MouseButton.Right);
                break;
            case 3:
                output.Click(MouseButton.Middle);
                break;
        }
    }

    private void StartDrag()
    {
        _dragging = true;
        output.Press(MouseButton.Left);
    }

    private void MovePointer(double dx, double dy, double dtMs)
    {
        var distance = Math.Sqrt(dx * dx + dy * dy);
        // Frames come at up to ~60 fps; guard against bursts arriving back to back.
        var speed = distance / (Math.Max(dtMs, 8) / 1000); // surface heights per second
        var boost = Math.Clamp((speed - 0.25) / 2.0, 0, 1);
        var gain = output.Sink.ScreenHeight * PointerScreens * output.PointerSpeed * (1 + PointerAcceleration * boost);

        _pointerRestX += dx * gain;
        _pointerRestY += dy * gain;
        var moveX = (int)Math.Truncate(_pointerRestX);
        var moveY = (int)Math.Truncate(_pointerRestY);
        _pointerRestX -= moveX;
        _pointerRestY -= moveY;

        if (moveX != 0 || moveY != 0) output.Sink.MoveMouse(moveX, moveY);
    }

    private void Scroll(double dx, double dy)
    {
        _scrollX += dx;
        _scrollY += dy;

        if (_axis == ScrollAxis.Undecided)
        {
            if (Distance(_scrollX, _scrollY, 0, 0) < AxisLockDistance) return;
            _axis = Math.Abs(_scrollX) > Math.Abs(_scrollY) ? ScrollAxis.Horizontal : ScrollAxis.Vertical;
            _wheelRest = 0;
            // Spend the travel that decided the axis, too.
            dx = _scrollX;
            dy = _scrollY;
        }

        // Content follows the fingers: fingers down reveals what is above (wheel up);
        // fingers right reveals what is to the left (wheel left, which is negative).
        var horizontal = _axis == ScrollAxis.Horizontal;
        _wheelRest += horizontal ? -dx * WheelPerHeight : dy * WheelPerHeight;

        var chunks = (int)(_wheelRest / WheelChunk);
        if (chunks == 0) return;

        var delta = chunks * WheelChunk;
        _wheelRest -= delta;
        output.Sink.Wheel(delta, horizontal);
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
