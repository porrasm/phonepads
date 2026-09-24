using System.Net;

namespace Phonepads.Dsu;

/// <summary>A datagram to send, returned rather than sent so the core stays free of sockets.</summary>
public readonly record struct DsuOutgoing(IPEndPoint To, byte[] Data);

/// <summary>
/// The protocol side of a DSU server, with no socket in it: feed it incoming datagrams and
/// pad reports, get back the datagrams to send. Tracks which clients asked for which slots
/// and forgets them once they stop asking.
/// </summary>
public sealed class DsuServerCore(uint serverId, TimeProvider clock)
{
    public const int SlotCount = 4;

    /// <summary>Clients re-subscribe about once a second; five seconds of silence means gone.</summary>
    public static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<IPEndPoint, Subscription> _clients = [];
    private readonly bool[] _connected = new bool[SlotCount];

    public uint ServerId { get; } = serverId;

    public int ClientCount => _clients.Count;

    /// <summary>The synthetic identifier a slot's pad presents as its MAC address.</summary>
    public static ulong MacFor(int slot) => 0x00_00_00_00_00_01UL + (ulong)slot;

    public void SetSlotConnected(int slot, bool connected)
    {
        if (slot is < 0 or >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
        _connected[slot] = connected;
    }

    public bool IsSlotConnected(int slot) => _connected[slot];

    /// <summary>Handles one client datagram. Anything malformed is dropped silently, as the protocol expects.</summary>
    public IReadOnlyList<DsuOutgoing> HandleDatagram(ReadOnlySpan<byte> datagram, IPEndPoint from)
    {
        Prune();

        if (!DsuPackets.TryParseRequest(datagram, out var request)) return [];

        switch (request.Type)
        {
            case DsuPackets.MessageType.Version:
                return [new DsuOutgoing(from, DsuPackets.BuildVersionResponse(ServerId))];

            case DsuPackets.MessageType.PortInfo:
            {
                var responses = new List<DsuOutgoing>(request.Slots.Count);
                foreach (var slot in request.Slots)
                {
                    if (slot >= SlotCount) continue;
                    responses.Add(new DsuOutgoing(from, DsuPackets.BuildPortInfo(ServerId, SlotInfo(slot))));
                }

                return responses;
            }

            case DsuPackets.MessageType.PadData:
                _clients[from] = new Subscription(request.Flags, request.Slot, request.Mac, clock.GetUtcNow());
                return [];

            default:
                return [];
        }
    }

    /// <summary>Packets carrying one slot's report to every client that asked for that slot.</summary>
    public IReadOnlyList<DsuOutgoing> Broadcast(in DsuPadReport report)
    {
        Prune();
        if (_clients.Count == 0) return [];

        byte[]? packet = null;
        var outgoing = new List<DsuOutgoing>();

        foreach (var (endpoint, subscription) in _clients)
        {
            if (!subscription.Wants(report.Slot)) continue;
            packet ??= DsuPackets.BuildPadData(ServerId, report);
            outgoing.Add(new DsuOutgoing(endpoint, packet));
        }

        return outgoing;
    }

    private DsuSlotInfo SlotInfo(int slot) => new(
        (byte)slot,
        _connected[slot] ? DsuSlotState.Connected : DsuSlotState.Disconnected,
        _connected[slot] ? DsuModel.FullGyro : DsuModel.None,
        _connected[slot] ? DsuConnectionType.Bluetooth : DsuConnectionType.None,
        MacFor(slot),
        _connected[slot] ? DsuBattery.Full : DsuBattery.NotApplicable);

    private void Prune()
    {
        var cutoff = clock.GetUtcNow() - ClientTimeout;
        foreach (var stale in _clients.Where(c => c.Value.LastSeen < cutoff).Select(c => c.Key).ToList())
            _clients.Remove(stale);
    }

    private readonly record struct Subscription(byte Flags, byte Slot, ulong Mac, DateTimeOffset LastSeen)
    {
        /// <summary>
        /// Flag bit 1 filters by slot, bit 2 by MAC, neither means everything. Both set means
        /// either match will do, which is how cemuhook itself reads it.
        /// </summary>
        public bool Wants(int slot)
        {
            if (Flags == 0) return true;
            var bySlot = (Flags & 0x01) != 0 && Slot == slot;
            var byMac = (Flags & 0x02) != 0 && Mac == MacFor(slot);
            return bySlot || byMac;
        }
    }
}
