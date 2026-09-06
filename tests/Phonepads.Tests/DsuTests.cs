using System.Buffers.Binary;
using System.Net;
using System.Text;
using Phonepads.Core;
using Phonepads.Dsu;
using Phonepads.Protocol;

namespace Phonepads.Tests;

public class DsuCrc32Tests
{
    [Fact]
    public void Matches_the_standard_check_value()
    {
        // The canonical CRC-32 test vector.
        Assert.Equal(0xCBF43926u, DsuCrc32.Compute("123456789"u8));
    }

    [Fact]
    public void Empty_input_is_zero()
    {
        Assert.Equal(0u, DsuCrc32.Compute([]));
    }
}

public class DsuPacketTests
{
    private const uint ServerId = 0xDEADBEEF;

    private static uint ReadU32(byte[] packet, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(offset));

    private static ushort ReadU16(byte[] packet, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(offset));

    private static float ReadF32(byte[] packet, int offset) => BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(offset));

    private static bool CrcIsValid(byte[] packet)
    {
        var copy = (byte[])packet.Clone();
        var claimed = ReadU32(copy, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(8), 0);
        return DsuCrc32.Compute(copy) == claimed;
    }

    [Fact]
    public void Port_info_is_exactly_the_size_dolphin_checks_and_carries_the_header()
    {
        var packet = DsuPackets.BuildPortInfo(ServerId, new DsuSlotInfo(
            2, DsuSlotState.Connected, DsuModel.FullGyro, DsuConnectionType.Bluetooth, 0x0000_0000_0003, DsuBattery.Full));

        Assert.Equal(32, packet.Length);
        Assert.Equal("DSUS", Encoding.ASCII.GetString(packet, 0, 4));
        Assert.Equal(1001, ReadU16(packet, 4));
        Assert.Equal(packet.Length - 16, ReadU16(packet, 6));
        Assert.Equal(ServerId, ReadU32(packet, 12));
        Assert.Equal(0x100001u, ReadU32(packet, 16));
        Assert.True(CrcIsValid(packet));

        Assert.Equal(2, packet[20]); // slot
        Assert.Equal(2, packet[21]); // connected
        Assert.Equal(2, packet[22]); // full gyro
        Assert.Equal(2, packet[23]); // bluetooth
        Assert.Equal(3, packet[24]); // mac, low byte first
        Assert.Equal(5, packet[30]); // battery full
        Assert.Equal(0, packet[31]); // padding
    }

    [Fact]
    public void Version_response_has_the_padding_dolphin_expects()
    {
        var packet = DsuPackets.BuildVersionResponse(ServerId);

        Assert.Equal(24, packet.Length);
        Assert.Equal(0x100000u, ReadU32(packet, 16));
        Assert.Equal(1001, ReadU16(packet, 20));
        Assert.True(CrcIsValid(packet));
    }

    [Fact]
    public void Pad_data_is_one_hundred_bytes_with_every_field_where_the_protocol_puts_it()
    {
        var report = new DsuPadReport
        {
            Slot = 1,
            PacketNumber = 0x01020304,
            Pad = new PadState
            {
                Buttons = PadButtons.A | PadButtons.X | PadButtons.Start | PadButtons.DpadUp | PadButtons.Guide,
                LeftStickX = short.MaxValue,
                LeftStickY = short.MaxValue,
                RightTrigger = 200,
            },
            Motion = new DsuMotion(0.1f, -0.9f, 0.2f, 12.5f, -3f, 0.4f),
            TimestampMicroseconds = 123456789,
        };

        var packet = DsuPackets.BuildPadData(ServerId, report);
        var p = packet.AsSpan(20).ToArray(); // payload after the message type

        Assert.Equal(100, packet.Length);
        Assert.Equal(0x100002u, ReadU32(packet, 16));
        Assert.True(CrcIsValid(packet));

        Assert.Equal(1, p[0]); // slot
        Assert.Equal(1, p[11]); // connected flag
        Assert.Equal(0x01020304u, ReadU32(p, 12));

        // Bitmask: dpad up (0x10) | options (0x08) in byte 1;
        // cross (0x20) | square (0x10) | R2 (0x02, the pulled trigger) in byte 2.
        Assert.Equal(0x18, p[16]);
        Assert.Equal(0x32, p[17]);
        Assert.Equal(1, p[18]); // PS / Home
        Assert.Equal(0, p[19]); // touch button

        Assert.Equal(255, p[20]); // left X full right
        Assert.Equal(1, p[21]); // left Y full up, on a wire where up is small
        Assert.Equal(128, p[22]); // right X centred
        Assert.Equal(128, p[23]); // right Y centred

        Assert.Equal(0, p[24]); // dpad left analog
        Assert.Equal(255, p[27]); // dpad up analog
        Assert.Equal(255, p[28]); // square (X)
        Assert.Equal(255, p[29]); // cross (A)
        Assert.Equal(0, p[30]); // circle
        Assert.Equal(0, p[31]); // triangle
        Assert.Equal(200, p[34]); // R2 analog
        Assert.Equal(0, p[35]); // L2 analog

        Assert.Equal(123456789ul, BinaryPrimitives.ReadUInt64LittleEndian(p.AsSpan(48)));
        Assert.Equal(0.1f, ReadF32(p, 56));
        Assert.Equal(-0.9f, ReadF32(p, 60));
        Assert.Equal(0.2f, ReadF32(p, 64));
        Assert.Equal(12.5f, ReadF32(p, 68)); // pitch
        Assert.Equal(-3f, ReadF32(p, 72)); // yaw
        Assert.Equal(0.4f, ReadF32(p, 76)); // roll
    }

    [Fact]
    public void A_pulled_trigger_also_sets_its_digital_bit()
    {
        var buttons = DsuPackets.ButtonsFrom(new PadState { LeftTrigger = 1, RightTrigger = 0 });

        Assert.True(buttons.HasFlag(DsuButtons.L2));
        Assert.False(buttons.HasFlag(DsuButtons.R2));
    }

    [Theory]
    [InlineData(PadButtons.A, DsuButtons.Cross)]
    [InlineData(PadButtons.B, DsuButtons.Circle)]
    [InlineData(PadButtons.X, DsuButtons.Square)]
    [InlineData(PadButtons.Y, DsuButtons.Triangle)]
    [InlineData(PadButtons.LeftBumper, DsuButtons.L1)]
    [InlineData(PadButtons.RightBumper, DsuButtons.R1)]
    [InlineData(PadButtons.Back, DsuButtons.Share)]
    [InlineData(PadButtons.Start, DsuButtons.Options)]
    [InlineData(PadButtons.LeftThumb, DsuButtons.L3)]
    [InlineData(PadButtons.RightThumb, DsuButtons.R3)]
    [InlineData(PadButtons.DpadUp, DsuButtons.DpadUp)]
    [InlineData(PadButtons.DpadDown, DsuButtons.DpadDown)]
    [InlineData(PadButtons.DpadLeft, DsuButtons.DpadLeft)]
    [InlineData(PadButtons.DpadRight, DsuButtons.DpadRight)]
    public void Xbox_buttons_land_on_their_dualshock_positions(PadButtons xbox, DsuButtons expected)
    {
        Assert.Equal(expected, DsuPackets.ButtonsFrom(new PadState { Buttons = xbox }));
    }

    [Fact]
    public void Guide_is_not_a_bitmask_button()
    {
        Assert.Equal(DsuButtons.None, DsuPackets.ButtonsFrom(new PadState { Buttons = PadButtons.Guide }));
    }

    [Theory]
    [InlineData(0, false, 128)]
    [InlineData(short.MaxValue, false, 255)]
    [InlineData(short.MinValue, false, 0)]
    [InlineData(short.MaxValue, true, 1)]
    [InlineData(short.MinValue, true, 255)]
    public void Stick_bytes_centre_at_128_and_can_be_inverted(short axis, bool invert, byte expected)
    {
        Assert.Equal(expected, DsuPackets.StickByte(axis, invert));
    }

    [Fact]
    public void A_port_info_request_round_trips()
    {
        var datagram = DsuPackets.BuildPortInfoRequest(0xABCD, 0, 1, 2, 3);

        Assert.True(DsuPackets.TryParseRequest(datagram, out var request));
        Assert.Equal(DsuPackets.MessageType.PortInfo, request.Type);
        Assert.Equal(0xABCDu, request.ClientId);
        Assert.Equal([0, 1, 2, 3], request.Slots);
    }

    [Fact]
    public void A_data_subscription_round_trips()
    {
        var datagram = DsuPackets.BuildPadDataRequest(7, flags: 1, slot: 2, mac: 0x0000_0000_0003);

        Assert.True(DsuPackets.TryParseRequest(datagram, out var request));
        Assert.Equal(DsuPackets.MessageType.PadData, request.Type);
        Assert.Equal(1, request.Flags);
        Assert.Equal(2, request.Slot);
        Assert.Equal(3ul, request.Mac);
    }

    [Fact]
    public void Corrupt_requests_are_rejected()
    {
        var good = DsuPackets.BuildPortInfoRequest(1, 0);

        var badMagic = (byte[])good.Clone();
        badMagic[0] = (byte)'X';
        Assert.False(DsuPackets.TryParseRequest(badMagic, out _));

        var badCrc = (byte[])good.Clone();
        badCrc[25] ^= 0xFF;
        Assert.False(DsuPackets.TryParseRequest(badCrc, out _));

        var badVersion = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(badVersion.AsSpan(4), 1000);
        Assert.False(DsuPackets.TryParseRequest(badVersion, out _));

        Assert.False(DsuPackets.TryParseRequest(good.AsSpan(0, 19), out _));
        Assert.False(DsuPackets.TryParseRequest([], out _));
    }

    [Fact]
    public void A_server_packet_is_not_mistaken_for_a_request()
    {
        var packet = DsuPackets.BuildVersionResponse(ServerId);

        Assert.False(DsuPackets.TryParseRequest(packet, out _));
    }
}

public class DsuServerCoreTests
{
    private static readonly IPEndPoint Dolphin = new(IPAddress.Loopback, 50000);
    private static readonly IPEndPoint Other = new(IPAddress.Loopback, 50001);

    private static DsuPadReport Report(int slot) => new() { Slot = slot, PacketNumber = 1 };

    [Fact]
    public void Answers_a_port_info_request_with_one_packet_per_slot()
    {
        var core = new DsuServerCore(42, new ManualClock());
        core.SetSlotConnected(1, true);

        var responses = core.HandleDatagram(DsuPackets.BuildPortInfoRequest(1, 0, 1, 2, 3), Dolphin);

        Assert.Equal(4, responses.Count);
        Assert.All(responses, r => Assert.Equal(Dolphin, r.To));
        Assert.Equal(0, responses[0].Data[21]); // slot 0 disconnected
        Assert.Equal(2, responses[1].Data[21]); // slot 1 connected
        Assert.Equal(2, responses[1].Data[22]); // and a full gyro
        Assert.Equal(1, responses[1].Data[20]); // reporting its own slot number
    }

    [Fact]
    public void Slot_numbers_in_responses_match_the_request()
    {
        var core = new DsuServerCore(42, new ManualClock());

        var responses = core.HandleDatagram(DsuPackets.BuildPortInfoRequest(1, 3, 1), Dolphin);

        Assert.Equal([3, 1], responses.Select(r => (int)r.Data[20]));
    }

    [Fact]
    public void Slots_beyond_four_are_ignored()
    {
        var core = new DsuServerCore(42, new ManualClock());

        var responses = core.HandleDatagram(DsuPackets.BuildPortInfoRequest(1, 0, 9), Dolphin);

        Assert.Single(responses);
    }

    [Fact]
    public void Answers_a_version_request()
    {
        var core = new DsuServerCore(42, new ManualClock());
        var request = DsuPackets.BuildPortInfoRequest(1, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(16), 0x100000);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), DsuCrc32.Compute(request));

        var responses = core.HandleDatagram(request, Dolphin);

        var response = Assert.Single(responses);
        Assert.Equal(24, response.Data.Length);
    }

    [Fact]
    public void Nothing_is_broadcast_until_someone_subscribes()
    {
        var core = new DsuServerCore(42, new ManualClock());
        core.SetSlotConnected(0, true);

        Assert.Empty(core.Broadcast(Report(0)));
    }

    [Fact]
    public void A_subscriber_to_everything_gets_every_slot()
    {
        var core = new DsuServerCore(42, new ManualClock());
        core.HandleDatagram(DsuPackets.BuildPadDataRequest(1, flags: 0, slot: 0, mac: 0), Dolphin);

        Assert.Single(core.Broadcast(Report(0)));
        Assert.Single(core.Broadcast(Report(3)));
    }

    [Fact]
    public void A_slot_subscriber_gets_only_that_slot()
    {
        var core = new DsuServerCore(42, new ManualClock());
        core.HandleDatagram(DsuPackets.BuildPadDataRequest(1, flags: 1, slot: 2, mac: 0), Dolphin);

        Assert.Empty(core.Broadcast(Report(0)));
        Assert.Single(core.Broadcast(Report(2)));
    }

    [Fact]
    public void A_mac_subscriber_gets_the_slot_with_that_mac()
    {
        var core = new DsuServerCore(42, new ManualClock());
        core.HandleDatagram(DsuPackets.BuildPadDataRequest(1, flags: 2, slot: 0, mac: DsuServerCore.MacFor(3)), Dolphin);

        Assert.Empty(core.Broadcast(Report(0)));
        Assert.Single(core.Broadcast(Report(3)));
    }

    [Fact]
    public void Each_subscriber_gets_its_own_copy_of_the_broadcast()
    {
        var core = new DsuServerCore(42, new ManualClock());
        core.HandleDatagram(DsuPackets.BuildPadDataRequest(1, 0, 0, 0), Dolphin);
        core.HandleDatagram(DsuPackets.BuildPadDataRequest(2, 0, 0, 0), Other);

        var sent = core.Broadcast(Report(0));

        Assert.Equal(2, sent.Count);
        Assert.Equal([Dolphin, Other], sent.Select(s => s.To).OrderBy(e => e.Port));
    }

    [Fact]
    public void Silent_subscribers_are_forgotten_after_five_seconds()
    {
        var clock = new ManualClock();
        var core = new DsuServerCore(42, clock);
        core.HandleDatagram(DsuPackets.BuildPadDataRequest(1, 0, 0, 0), Dolphin);

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Single(core.Broadcast(Report(0)));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(core.Broadcast(Report(0)));
        Assert.Equal(0, core.ClientCount);
    }

    [Fact]
    public void Resubscribing_keeps_a_client_alive()
    {
        var clock = new ManualClock();
        var core = new DsuServerCore(42, clock);
        core.HandleDatagram(DsuPackets.BuildPadDataRequest(1, 0, 0, 0), Dolphin);

        clock.Advance(TimeSpan.FromSeconds(4));
        core.HandleDatagram(DsuPackets.BuildPadDataRequest(1, 0, 0, 0), Dolphin);
        clock.Advance(TimeSpan.FromSeconds(4));

        Assert.Single(core.Broadcast(Report(0)));
    }

    [Fact]
    public void Garbage_neither_answers_nor_subscribes()
    {
        var core = new DsuServerCore(42, new ManualClock());

        var responses = core.HandleDatagram("hello there"u8, Dolphin);

        Assert.Empty(responses);
        Assert.Equal(0, core.ClientCount);
    }

    [Fact]
    public void Broadcast_packets_carry_the_report()
    {
        var core = new DsuServerCore(42, new ManualClock());
        core.HandleDatagram(DsuPackets.BuildPadDataRequest(1, 0, 0, 0), Dolphin);

        var sent = Assert.Single(core.Broadcast(new DsuPadReport
        {
            Slot = 2,
            PacketNumber = 99,
            Pad = new PadState { Buttons = PadButtons.A },
        }));

        Assert.Equal(2, sent.Data[20]);
        Assert.Equal(99u, BinaryPrimitives.ReadUInt32LittleEndian(sent.Data.AsSpan(32)));
        Assert.Equal(255, sent.Data[49]); // cross analog
    }
}

public class DsuPadHubTests
{
    private static readonly IPEndPoint Dolphin = new(IPAddress.Loopback, 50000);

    private sealed class Rig
    {
        public ManualClock Clock { get; } = new();
        public DsuServerCore Core { get; }
        public List<DsuOutgoing> Sent { get; } = [];
        public DsuPadHub Hub { get; }

        public Rig()
        {
            Core = new DsuServerCore(7, Clock);
            Hub = new DsuPadHub(Core, Sent.Add, Clock);
        }

        public void Subscribe() => Hub.HandleDatagram(DsuPackets.BuildPadDataRequest(1, 0, 0, 0), Dolphin);
    }

    [Fact]
    public void The_hub_is_the_wii_remote_backend_and_available_without_a_socket()
    {
        var rig = new Rig();

        Assert.Equal(PadBackend.WiiRemote, rig.Hub.Backend);
        Assert.True(rig.Hub.IsAvailable);
    }

    [Fact]
    public void Creating_a_pad_marks_its_slot_connected_and_disposing_unmarks_it()
    {
        var rig = new Rig();

        var pad = rig.Hub.Create(2);
        Assert.True(rig.Core.IsSlotConnected(2));
        Assert.Equal(PadBackend.WiiRemote, pad.Backend);
        Assert.Equal(2, pad.Slot);

        pad.Dispose();
        Assert.False(rig.Core.IsSlotConnected(2));
    }

    [Fact]
    public void Creating_the_same_slot_twice_returns_the_same_pad()
    {
        var rig = new Rig();

        Assert.Same(rig.Hub.Create(0), rig.Hub.Create(0));
    }

    [Fact]
    public void A_tick_streams_every_connected_slot_to_subscribers()
    {
        var rig = new Rig();
        rig.Subscribe();
        rig.Hub.Create(0);
        rig.Hub.Create(3);

        rig.Hub.Tick(DsuPadHub.EmitInterval);

        Assert.Equal(2, rig.Sent.Count);
        Assert.Equal([0, 3], rig.Sent.Select(s => (int)s.Data[20]).OrderBy(s => s));
    }

    [Fact]
    public void A_tick_with_no_pads_sends_nothing()
    {
        var rig = new Rig();
        rig.Subscribe();

        rig.Hub.Tick(DsuPadHub.EmitInterval);

        Assert.Empty(rig.Sent);
    }

    [Fact]
    public void The_stream_carries_the_latest_pad_state()
    {
        var rig = new Rig();
        rig.Subscribe();
        var pad = rig.Hub.Create(0);

        pad.Update(new PadState { Buttons = PadButtons.B });
        rig.Hub.Tick(DsuPadHub.EmitInterval);

        var packet = Assert.Single(rig.Sent).Data;
        Assert.Equal(255, packet[20 + 30]); // circle analog
    }

    [Fact]
    public void Idle_motion_streams_as_a_remote_lying_still()
    {
        var rig = new Rig();
        rig.Subscribe();
        rig.Hub.Create(0);

        rig.Hub.Tick(DsuPadHub.EmitInterval);

        var p = Assert.Single(rig.Sent).Data.AsSpan(20).ToArray();
        Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(56)));
        Assert.Equal(-1f, BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(60))); // gravity, DSU style
        Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(64)));
        Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(68)));
    }

    [Fact]
    public void Pushed_motion_reaches_the_stream_in_dolphins_frame()
    {
        var rig = new Rig();
        rig.Subscribe();
        var pad = rig.Hub.Create(0);

        // Nose lifting at 90 °/s about the phone's x axis for 100 ms, then one 100 ms tick.
        pad.PushMotion([new MotionSample(0, 0, 0, 1, 90, 0, 0), new MotionSample(100, 0, 0, 1, 90, 0, 0)]);
        rig.Hub.Tick(TimeSpan.FromMilliseconds(100));

        var p = Assert.Single(rig.Sent).Data.AsSpan(20).ToArray();
        Assert.Equal(90f, BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(68)), 3); // pitch
    }

    [Fact]
    public void Packet_numbers_increase_per_slot()
    {
        var rig = new Rig();
        rig.Subscribe();
        rig.Hub.Create(0);

        rig.Hub.Tick(DsuPadHub.EmitInterval);
        rig.Hub.Tick(DsuPadHub.EmitInterval);

        var numbers = rig.Sent.Select(s => BinaryPrimitives.ReadUInt32LittleEndian(s.Data.AsSpan(32))).ToList();
        Assert.Equal([0u, 1u], numbers);
    }

    [Fact]
    public void Reset_returns_the_slot_to_neutral()
    {
        var rig = new Rig();
        rig.Subscribe();
        var pad = rig.Hub.Create(0);
        pad.Update(new PadState { Buttons = PadButtons.A, LeftStickX = short.MaxValue });
        pad.PushMotion([new MotionSample(0, 0, 0, 1, 90, 0, 0), new MotionSample(100, 0, 0, 1, 90, 0, 0)]);

        pad.Reset();
        rig.Hub.Tick(DsuPadHub.EmitInterval);

        var p = Assert.Single(rig.Sent).Data.AsSpan(20).ToArray();
        Assert.Equal(0, p[29]); // cross released
        Assert.Equal(128, p[20]); // stick centred
        Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(68))); // no rotation
    }

    [Fact]
    public void Info_requests_are_answered_through_the_sender()
    {
        var rig = new Rig();
        rig.Hub.Create(1);

        rig.Hub.HandleDatagram(DsuPackets.BuildPortInfoRequest(1, 0, 1), Dolphin);

        Assert.Equal(2, rig.Sent.Count);
        Assert.Equal(2, rig.Sent[1].Data[21]); // slot 1 reports connected
    }
}

public class WiiMotionFrameTests
{
    [Fact]
    public void A_phone_lying_flat_is_a_remote_lying_flat()
    {
        var dsu = WiiMotionFrame.FromPhone(MotionOutput.AtRest);

        // Dolphin reads "Accel Up" as −accel_y, so gravity's reaction force is −1 on Y.
        Assert.Equal(0f, dsu.AccelX);
        Assert.Equal(-1f, dsu.AccelY);
        Assert.Equal(0f, dsu.AccelZ);
        Assert.Equal(0f, dsu.GyroPitch);
        Assert.Equal(0f, dsu.GyroYaw);
        Assert.Equal(0f, dsu.GyroRoll);
    }

    [Fact]
    public void Lifting_the_nose_is_pitch_up()
    {
        var dsu = WiiMotionFrame.FromPhone(new MotionOutput(0, 0, 1, RateX: 45, RateY: 0, RateZ: 0));

        Assert.Equal(45f, dsu.GyroPitch);
    }

    [Fact]
    public void Tipping_the_top_edge_rightward_is_roll_right()
    {
        var dsu = WiiMotionFrame.FromPhone(new MotionOutput(0, 0, 1, RateX: 0, RateY: 45, RateZ: 0));

        Assert.Equal(45f, dsu.GyroRoll);
    }

    [Fact]
    public void Turning_about_the_up_axis_is_yaw_the_other_way()
    {
        // Right-hand rotation about "up" swings the nose to the left; Dolphin's yaw is right-positive.
        var dsu = WiiMotionFrame.FromPhone(new MotionOutput(0, 0, 1, RateX: 0, RateY: 0, RateZ: 45));

        Assert.Equal(-45f, dsu.GyroYaw);
    }

    [Fact]
    public void Forward_and_rightward_acceleration_land_on_the_right_fields()
    {
        var forward = WiiMotionFrame.FromPhone(new MotionOutput(0, 1, 0, 0, 0, 0));
        var right = WiiMotionFrame.FromPhone(new MotionOutput(1, 0, 0, 0, 0, 0));

        Assert.Equal(1f, forward.AccelZ); // Dolphin: Accel Forward = +accel_z
        Assert.Equal(-1f, right.AccelX); // Dolphin: Accel Left = +accel_x, so right is negative
    }
}

public class DolphinProfileTests
{
    [Fact]
    public void Device_name_uses_the_slot_as_dolphin_indexes_dsu_pads()
    {
        Assert.Equal("DSUClient/2/Phonepads", DolphinProfile.DeviceName(2));
    }

    [Fact]
    public void Every_wii_button_is_bound_to_the_dsu_input_its_pad_target_produces()
    {
        var ini = DolphinProfile.Generate(DolphinProfile.Variant.Remote, 0);

        Assert.Contains("Device = DSUClient/0/Phonepads", ini);
        Assert.Contains("Buttons/A = `Cross`", ini);
        Assert.Contains("Buttons/B = `Circle`", ini);
        Assert.Contains("Buttons/1 = `Square`", ini);
        Assert.Contains("Buttons/2 = `Triangle`", ini);
        Assert.Contains("Buttons/- = `Share`", ini);
        Assert.Contains("Buttons/+ = `Options`", ini);
        Assert.Contains("Buttons/Home = `PS`", ini);
        Assert.Contains("D-Pad/Up = `Pad N`", ini);
        Assert.Contains("D-Pad/Left = `Pad W`", ini);
        Assert.Contains("IMUIR/Recenter = `R3`", ini);
        Assert.Contains("Extension = None", ini);
    }

    [Fact]
    public void Motion_inputs_and_gyro_pointing_are_wired()
    {
        var ini = DolphinProfile.Generate(DolphinProfile.Variant.Remote, 0);

        Assert.Contains("IMUAccelerometer/Up = `Accel Up`", ini);
        Assert.Contains("IMUAccelerometer/Backward = `Accel Backward`", ini);
        Assert.Contains("IMUGyroscope/Pitch Up = `Gyro Pitch Up`", ini);
        Assert.Contains("IMUGyroscope/Yaw Right = `Gyro Yaw Right`", ini);
        Assert.Contains("IMUIR/Enabled = True", ini);
    }

    [Fact]
    public void The_nunchuk_variant_attaches_one_with_the_stick_on_the_left_stick()
    {
        var ini = DolphinProfile.Generate(DolphinProfile.Variant.RemoteWithNunchuk, 1);

        Assert.Contains("Extension = Nunchuk", ini);
        Assert.Contains("Nunchuk/Buttons/C = `L1`", ini);
        Assert.Contains("Nunchuk/Buttons/Z = `L2`", ini);
        Assert.Contains("Nunchuk/Stick/Up = `Left Y-`", ini); // the wire's Y grows downward
        Assert.Contains("Nunchuk/Stick/Right = `Left X+`", ini);
        Assert.Contains("Device = DSUClient/1/Phonepads", ini);
    }

    [Fact]
    public void The_sideways_variant_turns_the_remote()
    {
        var ini = DolphinProfile.Generate(DolphinProfile.Variant.RemoteSideways, 0);

        Assert.Contains("Options/Sideways Wiimote = True", ini);
        Assert.DoesNotContain("Nunchuk/", ini);
    }

    [Fact]
    public void Every_single_button_target_in_the_wii_layout_has_a_dsu_input_name()
    {
        foreach (var target in WiiLayout.All.Where(t => t is not (PadTarget.Dpad or PadTarget.LeftStick)))
            Assert.False(string.IsNullOrEmpty(DolphinProfile.DsuInputName(target)));
    }

    [Fact]
    public void The_profile_and_the_wire_agree_on_every_button()
    {
        // Whatever DsuInputName says a target is called, pressing that target must set the
        // matching DSU button on the wire — otherwise the profile binds the wrong thing.
        var expectations = new Dictionary<PadTarget, DsuButtons>
        {
            [PadTarget.A] = DsuButtons.Cross,
            [PadTarget.B] = DsuButtons.Circle,
            [PadTarget.X] = DsuButtons.Square,
            [PadTarget.Y] = DsuButtons.Triangle,
            [PadTarget.LeftBumper] = DsuButtons.L1,
            [PadTarget.RightBumper] = DsuButtons.R1,
            [PadTarget.Back] = DsuButtons.Share,
            [PadTarget.Start] = DsuButtons.Options,
            [PadTarget.LeftThumbClick] = DsuButtons.L3,
            [PadTarget.RightThumbClick] = DsuButtons.R3,
        };

        var names = new Dictionary<DsuButtons, string>
        {
            [DsuButtons.Cross] = "Cross", [DsuButtons.Circle] = "Circle", [DsuButtons.Square] = "Square",
            [DsuButtons.Triangle] = "Triangle", [DsuButtons.L1] = "L1", [DsuButtons.R1] = "R1",
            [DsuButtons.Share] = "Share", [DsuButtons.Options] = "Options", [DsuButtons.L3] = "L3",
            [DsuButtons.R3] = "R3",
        };

        var schema = new Schema
        {
            Id = "one",
            Name = "One",
            Controls = [new SchemaControl { Id = "b", Type = ControlType.Button }],
        };

        foreach (var (target, wireButton) in expectations)
        {
            var mapping = new Mapping
            {
                SchemaId = "one",
                Controls = new Dictionary<string, ControlMapping> { ["b"] = new() { Target = target } },
            };
            var state = MappingEngine.Apply(schema, mapping, new Dictionary<string, ControlValue>
            {
                ["b"] = ControlValue.Button(true),
            });

            Assert.Equal(wireButton, DsuPackets.ButtonsFrom(state));
            Assert.Equal(names[wireButton], DolphinProfile.DsuInputName(target));
        }
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonepads-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void A_user_folder_resolves_to_its_profile_folder()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Config"));

            var target = DolphinProfile.ResolveProfileDirectory(dir);

            Assert.True(target.Ok, target.Message);
            Assert.Equal(Path.Combine(dir, "Config", "Profiles", "Wiimote"), target.Directory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_portable_install_resolves_into_its_user_folder()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Dolphin.exe"), "");
            File.WriteAllText(Path.Combine(dir, "portable.txt"), "");

            var target = DolphinProfile.ResolveProfileDirectory(dir);

            Assert.True(target.Ok, target.Message);
            Assert.Equal(Path.Combine(dir, "User", "Config", "Profiles", "Wiimote"), target.Directory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_non_portable_install_folder_is_refused_with_directions()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Dolphin.exe"), "");

            var target = DolphinProfile.ResolveProfileDirectory(dir);

            Assert.False(target.Ok);
            Assert.Contains("Documents", target.Message);
            Assert.Contains("portable.txt", target.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pointing_at_the_profile_folder_or_its_parents_works_too()
    {
        var dir = TempDir();
        try
        {
            var wiimote = Path.Combine(dir, "Config", "Profiles", "Wiimote");
            Directory.CreateDirectory(wiimote);

            Assert.Equal(wiimote, DolphinProfile.ResolveProfileDirectory(wiimote).Directory);
            Assert.Equal(wiimote, DolphinProfile.ResolveProfileDirectory(Path.Combine(dir, "Config", "Profiles")).Directory);
            Assert.Equal(wiimote, DolphinProfile.ResolveProfileDirectory(Path.Combine(dir, "Config")).Directory);
            Assert.Equal(wiimote, DolphinProfile.ResolveProfileDirectory(dir + Path.DirectorySeparatorChar).Directory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void An_unrelated_folder_is_refused()
    {
        var dir = TempDir();
        try
        {
            var target = DolphinProfile.ResolveProfileDirectory(dir);

            Assert.False(target.Ok);
            Assert.Contains("does not look like Dolphin", target.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Writes_a_file_per_variant_per_slot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonepads-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var written = DolphinProfile.WriteAll(dir);

            Assert.Equal(DolphinProfile.Variants.Count * DsuServerCore.SlotCount, written.Count);
            Assert.All(written, path => Assert.True(File.Exists(path)));
            Assert.Contains(written, p => p.EndsWith("Phonepads Wii Remote (P1).ini"));
            Assert.Contains(written, p => p.EndsWith("Phonepads Wii Remote + Nunchuk (P4).ini"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
