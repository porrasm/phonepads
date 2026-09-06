using System.Net;
using System.Net.Sockets;
using Phonepads.Core;
using Phonepads.Protocol;

namespace Phonepads.Dsu;

/// <summary>
/// Wii Remotes served over DSU. Owns the UDP socket, answers Dolphin's discovery and
/// subscription traffic, and streams every connected slot at a steady rate whether or not
/// the phone is moving — Dolphin's gyro calibration and its one-second timeout both need
/// the stream to keep flowing.
/// </summary>
public sealed class DsuPadHub : IVirtualPadHub
{
    public const int DefaultPort = 26760;

    /// <summary>Above Dolphin's 25 Hz calibration floor and matching the phone's sensor rate.</summary>
    public static readonly TimeSpan EmitInterval = TimeSpan.FromMilliseconds(1000d / 60);

    private readonly Lock _gate = new();
    private readonly Dictionary<int, DsuPad> _pads = [];
    private readonly DsuServerCore _core;
    private readonly Action<DsuOutgoing> _send;
    private readonly TimeProvider _clock;
    private readonly long _startTimestamp;
    private readonly UdpClient? _socket;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Test seam: no socket, sends go to <paramref name="send"/>, time is <paramref name="clock"/>.</summary>
    internal DsuPadHub(DsuServerCore core, Action<DsuOutgoing> send, TimeProvider clock)
    {
        _core = core;
        _send = send;
        _clock = clock;
        _startTimestamp = clock.GetTimestamp();
    }

    private DsuPadHub(UdpClient socket, int port)
        : this(new DsuServerCore((uint)Random.Shared.Next(), TimeProvider.System), _ => { }, TimeProvider.System)
    {
        _socket = socket;
        _send = outgoing =>
        {
            try
            {
                socket.Send(outgoing.Data, outgoing.Data.Length, outgoing.To);
            }
            catch (SocketException)
            {
                // A client that vanished mid-send is not our problem.
            }
        };
        EndPoint = $"127.0.0.1:{port}";

        _ = Task.Run(ReceiveLoopAsync);
        _ = Task.Run(EmitLoopAsync);
    }

    private DsuPadHub(string unavailableReason)
        : this(new DsuServerCore(0, TimeProvider.System), _ => { }, TimeProvider.System)
    {
        UnavailableReason = unavailableReason;
    }

    public PadBackend Backend => PadBackend.WiiRemote;

    public bool IsAvailable => UnavailableReason is null;

    public string? UnavailableReason { get; }

    /// <summary>Where Dolphin should be pointed, once listening.</summary>
    public string? EndPoint { get; }

    /// <summary>
    /// Binds the DSU port on loopback. Never throws: if another DSU server (DS4Windows, say)
    /// already owns the port, the hub reports itself unavailable and says why.
    /// </summary>
    public static DsuPadHub Start(int port = DefaultPort)
    {
        try
        {
            var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            return new DsuPadHub(socket, port);
        }
        catch (SocketException ex)
        {
            return new DsuPadHub(
                $"Wii Remote mode is off: UDP port {port} could not be opened ({ex.SocketErrorCode}). "
                + "Another DSU server such as DS4Windows may be using it; close it or change the port in settings.");
        }
    }

    public IVirtualPad Create(int slot)
    {
        if (slot is < 0 or >= DsuServerCore.SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));

        lock (_gate)
        {
            if (_pads.TryGetValue(slot, out var existing)) return existing;

            var pad = new DsuPad(this, slot);
            _pads[slot] = pad;
            _core.SetSlotConnected(slot, true);
            return pad;
        }
    }

    internal void HandleDatagram(ReadOnlySpan<byte> datagram, IPEndPoint from)
    {
        IReadOnlyList<DsuOutgoing> responses;
        lock (_gate) responses = _core.HandleDatagram(datagram, from);
        foreach (var response in responses) _send(response);
    }

    /// <summary>One emission for every connected slot, covering <paramref name="elapsed"/> of wall clock.</summary>
    internal void Tick(TimeSpan elapsed)
    {
        var timestamp = (ulong)_clock.GetElapsedTime(_startTimestamp).TotalMicroseconds;

        List<DsuOutgoing> outgoing = [];
        lock (_gate)
        {
            foreach (var pad in _pads.Values)
                outgoing.AddRange(_core.Broadcast(pad.BuildReport(elapsed, timestamp)));
        }

        foreach (var packet in outgoing) _send(packet);
    }

    private void Remove(int slot)
    {
        lock (_gate)
        {
            if (_pads.Remove(slot)) _core.SetSlotConnected(slot, false);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        if (_socket is null) return;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _socket.ReceiveAsync(_cts.Token);
                HandleDatagram(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                // ICMP unreachable from a departed client surfaces here on Windows; keep listening.
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private async Task EmitLoopAsync()
    {
        using var timer = new PeriodicTimer(EmitInterval);
        var last = _clock.GetTimestamp();

        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                var now = _clock.GetTimestamp();
                Tick(_clock.GetElapsedTime(last, now));
                last = now;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_gate)
        {
            foreach (var slot in _pads.Keys.ToList()) _core.SetSlotConnected(slot, false);
            _pads.Clear();
        }

        _socket?.Dispose();
        _cts.Dispose();
    }

    /// <summary>One slot: the latest pad state plus the motion integrator feeding its stream.</summary>
    private sealed class DsuPad(DsuPadHub hub, int slot) : IVirtualPad
    {
        private readonly MotionIntegrator _motion = new();
        private PadState _state;
        private uint _packetNumber;
        private bool _disposed;

        public int Slot => slot;

        public PadBackend Backend => PadBackend.WiiRemote;

        /// <summary>DSU carries no rumble, so this never fires.</summary>
        public event Action<byte, byte>? RumbleChanged
        {
            add { }
            remove { }
        }

        public void Update(PadState state)
        {
            lock (hub._gate) _state = state;
        }

        public void PushMotion(ReadOnlySpan<MotionSample> samples) => _motion.Push(samples);

        public void Reset()
        {
            lock (hub._gate) _state = PadState.Neutral;
            _motion.Reset();
        }

        public DsuPadReport BuildReport(TimeSpan elapsed, ulong timestampMicroseconds) => new()
        {
            Slot = slot,
            Connected = !_disposed,
            PacketNumber = _packetNumber++,
            Pad = _state,
            Motion = WiiMotionFrame.FromPhone(_motion.Emit(elapsed)),
            TimestampMicroseconds = timestampMicroseconds,
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            hub.Remove(slot);
        }
    }
}
