using Phonepads.Protocol;

namespace MobileKbm.Core;

/// <summary>
/// Turns what the phones send into keyboard and mouse input on this PC. Input frames are
/// full controller snapshots, so every button is edge-detected against the previous frame;
/// a clock tick drives what has no frames of its own (scrolling, key repeat, hold-to-drag).
/// Everything held is released on pause, on a dropped phone and on a lost session — a
/// remote that leaves a key stuck down is worse than no remote.
/// </summary>
public sealed class KbmController
{
    /// <summary>Held keys start repeating after this long, like a real keyboard.</summary>
    internal const double RepeatDelayMs = 450;

    internal const double RepeatIntervalMs = 33;

    /// <summary>
    /// Repeat stops after this long even if the button still reads as held. A phone that
    /// vanishes mid-press is only noticed by the service after up to a minute; a Backspace
    /// repeating for that long would eat a document.
    /// </summary>
    internal const double RepeatCutoffMs = 6000;

    private readonly HeldInput _held;
    private readonly IReadOnlyDictionary<string, KbmSchema> _schemas;
    private readonly KbmSchema _default;
    private readonly TimeProvider _time;
    private readonly long _epoch;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PlayerInput> _players = new(StringComparer.Ordinal);
    private bool _paused;

    public KbmController(IInputSink sink, IReadOnlyList<KbmSchema> schemas, TimeProvider? time = null)
    {
        _held = new HeldInput(sink);
        _schemas = schemas.ToDictionary(s => s.Id, StringComparer.Ordinal);
        _default = schemas[0];
        _time = time ?? TimeProvider.System;
        _epoch = _time.GetTimestamp();
    }

    /// <summary>Milliseconds on the controller's own clock, for callers feeding it frames.</summary>
    public double Now => _time.GetElapsedTime(_epoch).TotalMilliseconds;

    /// <summary>While paused nothing reaches the PC, and whatever was held is let go.</summary>
    public bool Paused
    {
        get
        {
            lock (_gate) return _paused;
        }
        set
        {
            lock (_gate)
            {
                _paused = value;
                if (value) ReleaseEverything();
            }
        }
    }

    /// <summary>Slowest and fastest <see cref="PointerSpeed"/> the settings offer.</summary>
    public const double MinPointerSpeed = 0.25;
    public const double MaxPointerSpeed = 4;

    /// <summary>Multiplies touchpad pointer motion: 1 is the default feel. Scrolling is unaffected.</summary>
    public double PointerSpeed
    {
        get
        {
            lock (_gate) return _held.PointerSpeed;
        }
        set
        {
            lock (_gate) _held.PointerSpeed = Math.Clamp(double.IsFinite(value) ? value : 1, MinPointerSpeed, MaxPointerSpeed);
        }
    }

    /// <summary>Adds or refreshes a phone. A phone that dropped, or switched layout, lets go of everything it held.</summary>
    public void SetPlayer(string playerId, string? schemaId, bool connected)
    {
        lock (_gate)
        {
            var player = GetOrAdd(playerId);
            var schema = Resolve(schemaId);
            if (!connected || !ReferenceEquals(player.Schema, schema)) player.Release(_held);
            player.Schema = schema;
        }
    }

    public void RemovePlayer(string playerId)
    {
        lock (_gate)
        {
            if (_players.Remove(playerId, out var player)) player.Release(_held);
        }
    }

    /// <summary>Lets go of everything but remembers the phones — for a dropped connection.</summary>
    public void ReleaseAll()
    {
        lock (_gate) ReleaseEverything();
    }

    /// <summary>Lets go of everything and forgets every phone — for a session that is gone.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            ReleaseEverything();
            _players.Clear();
        }
    }

    public void OnInput(string playerId, long seq, IReadOnlyDictionary<string, ControlValue> controls, double now)
    {
        lock (_gate)
        {
            if (_paused) return;

            var player = GetOrAdd(playerId);
            // Frames can overtake each other; only the newest counts.
            if (seq <= player.LastSeq) return;
            player.LastSeq = seq;

            foreach (var (controlId, value) in controls)
            {
                // Ids the layout does not have — say, a frame from the one just switched away from — are ignored.
                var action = player.Schema.ActionFor(controlId);
                if (action is null) continue;

                switch (action)
                {
                    case KeyAction or MouseButtonAction when value.Kind == ControlValueKind.Button:
                        player.SetHeld(_held, controlId, action, value.Pressed, now);
                        break;

                    case ScrollStripAction when value.Kind == ControlValueKind.Touches:
                        player.Strip(controlId, _held).Update(value.Touches);
                        break;

                    case ArrowPadAction when value.Kind == ControlValueKind.Dpad:
                        player.SetArrows(_held, controlId, value.Dpad, now);
                        break;

                    case TouchSurfaceAction surface when value.Kind == ControlValueKind.Touches:
                        player.Surface(controlId, surface, _held).Update(value.Touches, now);
                        break;
                }
            }
        }
    }

    /// <summary>Types what a phone sent from a text control. It arrives whole, when the player presses Send.</summary>
    public void OnText(string playerId, string controlId, string text)
    {
        lock (_gate)
        {
            if (_paused || text.Length == 0) return;
            _held.Sink.TypeText(text);
        }
    }

    /// <summary>Advances everything time-driven. Call it every frame or so (~60 Hz).</summary>
    public void Tick(double now)
    {
        lock (_gate)
        {
            if (_paused) return;

            foreach (var player in _players.Values) player.Tick(_held, now);
        }
    }

    /// <summary>Ticks on the controller's own clock until cancelled.</summary>
    public async Task RunTickerAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(15), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct)) Tick(Now);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private PlayerInput GetOrAdd(string playerId)
    {
        if (!_players.TryGetValue(playerId, out var player))
        {
            player = new PlayerInput(_default);
            _players[playerId] = player;
        }

        return player;
    }

    private KbmSchema Resolve(string? schemaId) =>
        schemaId is not null && _schemas.TryGetValue(schemaId, out var schema) ? schema : _default;

    private void ReleaseEverything()
    {
        foreach (var player in _players.Values) player.Release(_held);
        // Belt and braces: whatever the bookkeeping says, nothing stays down.
        _held.ReleaseAll();
    }

    /// <summary>One phone's controls as last seen, and what they are holding on the PC.</summary>
    private sealed class PlayerInput(KbmSchema schema)
    {
        private sealed class Held(IReadOnlyList<Key> keys, MouseButton? button, bool repeat, double since)
        {
            public IReadOnlyList<Key> Keys { get; } = keys;
            public MouseButton? Button { get; } = button;
            public bool Repeat { get; } = repeat;
            public double Since { get; } = since;
            public double NextRepeat { get; set; } = since + RepeatDelayMs;
        }

        private static readonly Key[] Arrows = [Key.Left, Key.Right, Key.Up, Key.Down];

        private readonly Dictionary<string, Held> _holding = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TouchpadGestures> _surfaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScrollStrip> _strips = new(StringComparer.Ordinal);

        public KbmSchema Schema { get; set; } = schema;

        public long LastSeq { get; set; } = long.MinValue;

        public void SetHeld(HeldInput output, string key, KbmAction action, bool pressed, double now)
        {
            var isHeld = _holding.ContainsKey(key);
            if (pressed == isHeld) return;

            if (!pressed)
            {
                Let(output, key);
                return;
            }

            var held = action switch
            {
                KeyAction keys => new Held(keys.Keys, null, keys.Repeat, now),
                MouseButtonAction mouse => new Held([], mouse.Button, false, now),
                _ => null,
            };
            if (held is null) return;

            _holding[key] = held;
            foreach (var k in held.Keys) output.Press(k);
            if (held.Button is { } button) output.Press(button);
        }

        /// <summary>A dpad holds one arrow, or two on a diagonal, each repeating on its own.</summary>
        public void SetArrows(HeldInput output, string controlId, DpadDirection direction, double now)
        {
            var (x, y) = ControlValue.DpadToVector(direction);
            foreach (var arrow in Arrows)
            {
                var on = arrow switch
                {
                    Key.Left => x < 0,
                    Key.Right => x > 0,
                    Key.Up => y < 0,
                    _ => y > 0,
                };
                SetHeld(output, controlId + "/" + arrow, new KeyAction([arrow], Repeat: true), on, now);
            }
        }

        public TouchpadGestures Surface(string controlId, TouchSurfaceAction action, HeldInput output)
        {
            if (!_surfaces.TryGetValue(controlId, out var surface))
            {
                surface = new TouchpadGestures(action.Aspect, output);
                _surfaces[controlId] = surface;
            }

            return surface;
        }

        public ScrollStrip Strip(string controlId, HeldInput output)
        {
            if (!_strips.TryGetValue(controlId, out var strip))
            {
                strip = new ScrollStrip(output);
                _strips[controlId] = strip;
            }

            return strip;
        }

        public void Tick(HeldInput output, double now)
        {
            foreach (var held in _holding.Values)
            {
                if (!held.Repeat || now - held.Since > RepeatCutoffMs || now < held.NextRepeat) continue;
                output.Repeat(held.Keys[^1]);
                // After a stall, carry on at the normal rate rather than firing a burst.
                held.NextRepeat = Math.Max(held.NextRepeat + RepeatIntervalMs, now);
            }

            foreach (var surface in _surfaces.Values) surface.Tick(now);
        }

        public void Release(HeldInput output)
        {
            foreach (var key in _holding.Keys.ToList()) Let(output, key);
            foreach (var surface in _surfaces.Values) surface.Reset();
            _surfaces.Clear();
            _strips.Clear();
        }

        private void Let(HeldInput output, string key)
        {
            if (!_holding.Remove(key, out var held)) return;
            if (held.Button is { } button) output.Release(button);
            for (var i = held.Keys.Count - 1; i >= 0; i--) output.Release(held.Keys[i]);
        }
    }
}
