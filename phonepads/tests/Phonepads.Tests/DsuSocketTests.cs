using System.Net;
using System.Net.Sockets;
using Phonepads.Core;
using Phonepads.Dsu;

namespace Phonepads.Tests;

/// <summary>
/// The one test that goes through a real UDP socket: discovery, subscription and the timed
/// stream, exactly as Dolphin would drive them. Everything else about DSU is covered without
/// sockets; this proves the loops around the core actually run.
/// </summary>
public class DsuSocketTests
{
    [Fact]
    public async Task Discovery_subscription_and_streaming_work_over_a_real_socket()
    {
        var (hub, port) = StartOnAFreePort();
        using (hub)
        {
            Assert.True(hub.IsAvailable, hub.UnavailableReason);
            Assert.Equal($"127.0.0.1:{port}", hub.EndPoint);

            using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var server = new IPEndPoint(IPAddress.Loopback, port);

            using var pad = hub.Create(1);

            // Discovery: one reply per asked slot, and slot 1 shows up connected.
            await client.SendAsync(DsuPackets.BuildPortInfoRequest(1, 0, 1, 2, 3), server);
            var infos = new List<byte[]>();
            for (var i = 0; i < 4; i++) infos.Add(await ReceiveAsync(client));

            Assert.All(infos, d => Assert.Equal(DsuPackets.PortInfoPacketLength, d.Length));
            Assert.Equal((byte)DsuSlotState.Connected, infos.Single(d => d[20] == 1)[21]);
            Assert.Equal((byte)DsuSlotState.Disconnected, infos.Single(d => d[20] == 0)[21]);

            // Subscription: the emit loop streams on its own, and carries what the pad holds.
            pad.Update(new PadState { Buttons = PadButtons.A });
            await client.SendAsync(DsuPackets.BuildPadDataRequest(1, 0, 0, 0), server);

            byte[]? withA = null;
            for (var i = 0; i < 30 && withA is null; i++)
            {
                var packet = await ReceiveAsync(client);
                Assert.Equal(DsuPackets.PadDataPacketLength, packet.Length);
                Assert.Equal(1, packet[20]);
                if (packet[20 + 29] == 255) withA = packet;
            }

            Assert.NotNull(withA);
        }
    }

    [Fact]
    public void A_port_already_in_use_makes_the_hub_unavailable_rather_than_throwing()
    {
        var (first, port) = StartOnAFreePort();
        using (first)
        {
            using var second = DsuPadHub.Start(port);

            Assert.False(second.IsAvailable);
            Assert.Contains(port.ToString(), second.UnavailableReason);
        }
    }

    private static (DsuPadHub Hub, int Port) StartOnAFreePort()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var port = Random.Shared.Next(40000, 60000);
            var hub = DsuPadHub.Start(port);
            if (hub.IsAvailable) return (hub, port);
            hub.Dispose();
        }

        throw new InvalidOperationException("Could not find a free UDP port for the test.");
    }

    private static async Task<byte[]> ReceiveAsync(UdpClient client)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        return (await client.ReceiveAsync(timeout.Token)).Buffer;
    }
}
