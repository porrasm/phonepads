using System.Buffers.Binary;
using System.Text;
using Phonepads.Core;

namespace Phonepads.Dsu;

public enum DsuSlotState : byte
{
    Disconnected = 0,
    Reserved = 1,
    Connected = 2,
}

public enum DsuModel : byte
{
    None = 0,
    PartialGyro = 1,
    FullGyro = 2,
}

public enum DsuConnectionType : byte
{
    None = 0,
    Usb = 1,
    Bluetooth = 2,
}

public enum DsuBattery : byte
{
    NotApplicable = 0x00,
    Dying = 0x01,
    Low = 0x02,
    Medium = 0x03,
    High = 0x04,
    Full = 0x05,
    Charging = 0xEE,
    Charged = 0xEF,
}

/// <summary>
/// The two button bitmask bytes as one value: the first wire byte in the low eight bits, the
/// second in the high eight. Bit positions follow the DualShock 4 HID report the protocol
/// was modelled on.
/// </summary>
[Flags]
public enum DsuButtons : ushort
{
    None = 0,
    Share = 0x0001,
    L3 = 0x0002,
    R3 = 0x0004,
    Options = 0x0008,
    DpadUp = 0x0010,
    DpadRight = 0x0020,
    DpadDown = 0x0040,
    DpadLeft = 0x0080,
    L2 = 0x0100,
    R2 = 0x0200,
    L1 = 0x0400,
    R1 = 0x0800,
    Square = 0x1000,
    Cross = 0x2000,
    Circle = 0x4000,
    Triangle = 0x8000,
}

/// <summary>The eleven-byte controller header shared by info and data responses.</summary>
public readonly record struct DsuSlotInfo(
    byte Slot,
    DsuSlotState State,
    DsuModel Model,
    DsuConnectionType Connection,
    ulong Mac,
    DsuBattery Battery);

/// <summary>One slot's complete state, ready to be encoded into a data packet.</summary>
public readonly record struct DsuPadReport
{
    public required int Slot { get; init; }
    public bool Connected { get; init; } = true;
    public required uint PacketNumber { get; init; }
    public PadState Pad { get; init; }
    public DsuMotion Motion { get; init; }

    /// <summary>Motion timestamp in microseconds. Dolphin ignores it; other clients may not.</summary>
    public ulong TimestampMicroseconds { get; init; }

    public DsuPadReport()
    {
    }
}

/// <summary>A parsed client request.</summary>
public readonly record struct DsuRequest(
    DsuPackets.MessageType Type,
    uint ClientId,
    IReadOnlyList<int> Slots,
    byte Flags,
    byte Slot,
    ulong Mac);

/// <summary>
/// Encodes and decodes DSU (cemuhook) datagrams byte for byte. Everything is little-endian.
/// Sizes matter: Dolphin computes the CRC over <c>sizeof</c> its own struct for each message,
/// so a packet one byte longer or shorter than it expects is silently dropped.
/// </summary>
public static class DsuPackets
{
    public enum MessageType : uint
    {
        Version = 0x100000,
        PortInfo = 0x100001,
        PadData = 0x100002,
    }

    public const ushort ProtocolVersion = 1001;

    /// <summary>Bytes before the message type: magic, version, length, CRC, sender id.</summary>
    public const int HeaderLength = 16;

    public const int PortInfoPacketLength = HeaderLength + 4 + 12;
    public const int PadDataPacketLength = HeaderLength + 4 + 80;
    /// <summary>Type, u16 version, and two bytes of padding — Dolphin's struct is 24 bytes.</summary>
    public const int VersionPacketLength = HeaderLength + 4 + 4;

    /// <summary>
    /// The DualShock 4 reports stick Y with 0 at the top, and the protocol kept that: Dolphin
    /// names the field <c>left_stick_y_inverted</c> and flips it. Our pad model is up-positive.
    /// </summary>
    public const bool WireStickYPointsDown = true;

    private static ReadOnlySpan<byte> ServerMagic => "DSUS"u8;

    private static ReadOnlySpan<byte> ClientMagic => "DSUC"u8;

    // ---- Encoding ----

    public static byte[] BuildVersionResponse(uint serverId)
    {
        var packet = new byte[VersionPacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), (uint)MessageType.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20), ProtocolVersion);
        Finish(packet, serverId);
        return packet;
    }

    public static byte[] BuildPortInfo(uint serverId, in DsuSlotInfo info)
    {
        var packet = new byte[PortInfoPacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), (uint)MessageType.PortInfo);
        WriteSlotInfo(packet.AsSpan(20), info);
        // Byte 31 is the trailing zero the protocol reserves; left as is.
        Finish(packet, serverId);
        return packet;
    }

    public static byte[] BuildPadData(uint serverId, in DsuPadReport report)
    {
        var packet = new byte[PadDataPacketLength];
        var payload = packet.AsSpan(20);

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), (uint)MessageType.PadData);

        WriteSlotInfo(payload, new DsuSlotInfo(
            (byte)report.Slot,
            report.Connected ? DsuSlotState.Connected : DsuSlotState.Disconnected,
            DsuModel.FullGyro,
            DsuConnectionType.Bluetooth,
            DsuServerCore.MacFor(report.Slot),
            DsuBattery.Full));

        payload[11] = report.Connected ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload[12..], report.PacketNumber);

        var pad = report.Pad;
        var buttons = ButtonsFrom(pad);
        payload[16] = (byte)((ushort)buttons & 0xFF);
        payload[17] = (byte)((ushort)buttons >> 8);
        payload[18] = pad.IsPressed(PadButtons.Guide) ? (byte)1 : (byte)0; // PS / Home
        payload[19] = 0; // touchpad click

        payload[20] = StickByte(pad.LeftStickX, invert: false);
        payload[21] = StickByte(pad.LeftStickY, invert: WireStickYPointsDown);
        payload[22] = StickByte(pad.RightStickX, invert: false);
        payload[23] = StickByte(pad.RightStickY, invert: WireStickYPointsDown);

        // Dolphin reads every button from these analog bytes, not from the bitmask.
        payload[24] = Digital(buttons, DsuButtons.DpadLeft);
        payload[25] = Digital(buttons, DsuButtons.DpadDown);
        payload[26] = Digital(buttons, DsuButtons.DpadRight);
        payload[27] = Digital(buttons, DsuButtons.DpadUp);
        payload[28] = Digital(buttons, DsuButtons.Square);
        payload[29] = Digital(buttons, DsuButtons.Cross);
        payload[30] = Digital(buttons, DsuButtons.Circle);
        payload[31] = Digital(buttons, DsuButtons.Triangle);
        payload[32] = Digital(buttons, DsuButtons.R1);
        payload[33] = Digital(buttons, DsuButtons.L1);
        payload[34] = pad.RightTrigger;
        payload[35] = pad.LeftTrigger;

        // Two touch points, 36..47: all zero — no touchpad on a phone controller.

        BinaryPrimitives.WriteUInt64LittleEndian(payload[48..], report.TimestampMicroseconds);
        BinaryPrimitives.WriteSingleLittleEndian(payload[56..], report.Motion.AccelX);
        BinaryPrimitives.WriteSingleLittleEndian(payload[60..], report.Motion.AccelY);
        BinaryPrimitives.WriteSingleLittleEndian(payload[64..], report.Motion.AccelZ);
        BinaryPrimitives.WriteSingleLittleEndian(payload[68..], report.Motion.GyroPitch);
        BinaryPrimitives.WriteSingleLittleEndian(payload[72..], report.Motion.GyroYaw);
        BinaryPrimitives.WriteSingleLittleEndian(payload[76..], report.Motion.GyroRoll);

        Finish(packet, serverId);
        return packet;
    }

    /// <summary>
    /// Places Xbox-shaped buttons on the DualShock-shaped wire: same positions, different
    /// names. Triggers set their digital bit whenever they are pulled at all.
    /// </summary>
    public static DsuButtons ButtonsFrom(PadState pad)
    {
        var buttons = DsuButtons.None;

        void Map(PadButtons from, DsuButtons to)
        {
            if (pad.IsPressed(from)) buttons |= to;
        }

        Map(PadButtons.A, DsuButtons.Cross);
        Map(PadButtons.B, DsuButtons.Circle);
        Map(PadButtons.X, DsuButtons.Square);
        Map(PadButtons.Y, DsuButtons.Triangle);
        Map(PadButtons.LeftBumper, DsuButtons.L1);
        Map(PadButtons.RightBumper, DsuButtons.R1);
        Map(PadButtons.Back, DsuButtons.Share);
        Map(PadButtons.Start, DsuButtons.Options);
        Map(PadButtons.LeftThumb, DsuButtons.L3);
        Map(PadButtons.RightThumb, DsuButtons.R3);
        Map(PadButtons.DpadUp, DsuButtons.DpadUp);
        Map(PadButtons.DpadDown, DsuButtons.DpadDown);
        Map(PadButtons.DpadLeft, DsuButtons.DpadLeft);
        Map(PadButtons.DpadRight, DsuButtons.DpadRight);

        if (pad.LeftTrigger > 0) buttons |= DsuButtons.L2;
        if (pad.RightTrigger > 0) buttons |= DsuButtons.R2;

        return buttons;
    }

    /// <summary>
    /// Signed 16-bit axis to the wire's 0-255 with 128 at centre. Both ranges are lopsided
    /// (-32768..32767 and 0..255 around 128), so each half is scaled by its own extent and full
    /// deflection lands exactly on 0 or 255.
    /// </summary>
    public static byte StickByte(short axis, bool invert)
    {
        var scaled = axis < 0 ? axis * 128d / 32768 : axis * 127d / 32767;
        if (invert) scaled = -scaled;
        return (byte)Math.Clamp(Math.Round(128 + scaled), 0, 255);
    }

    private static byte Digital(DsuButtons buttons, DsuButtons button) =>
        (buttons & button) != 0 ? (byte)255 : (byte)0;

    private static void WriteSlotInfo(Span<byte> target, in DsuSlotInfo info)
    {
        target[0] = info.Slot;
        target[1] = (byte)info.State;
        target[2] = (byte)info.Model;
        target[3] = (byte)info.Connection;
        for (var i = 0; i < 6; i++)
            target[4 + i] = (byte)(info.Mac >> (8 * i));
        target[10] = (byte)info.Battery;
    }

    /// <summary>Writes the header and stamps the CRC. Length counts everything after the 16-byte header.</summary>
    private static void Finish(byte[] packet, uint serverId)
    {
        ServerMagic.CopyTo(packet);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), (ushort)(packet.Length - HeaderLength));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), serverId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), DsuCrc32.Compute(packet));
    }

    // ---- Decoding ----

    /// <summary>
    /// Parses a client datagram. Returns false for anything that is not a well-formed request
    /// from a client: wrong magic or version, a bad CRC, or a truncated body.
    /// </summary>
    public static bool TryParseRequest(ReadOnlySpan<byte> datagram, out DsuRequest request)
    {
        request = default;
        if (datagram.Length < HeaderLength + 4) return false;
        if (!datagram[..4].SequenceEqual(ClientMagic)) return false;
        if (BinaryPrimitives.ReadUInt16LittleEndian(datagram[4..]) != ProtocolVersion) return false;

        var declared = BinaryPrimitives.ReadUInt16LittleEndian(datagram[6..]);
        if (declared > datagram.Length - HeaderLength) return false;
        var packet = datagram[..(HeaderLength + declared)];

        if (!CrcMatches(packet)) return false;

        var clientId = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]);
        var type = (MessageType)BinaryPrimitives.ReadUInt32LittleEndian(packet[16..]);
        var body = packet[20..];

        switch (type)
        {
            case MessageType.Version:
                request = new DsuRequest(type, clientId, [], 0, 0, 0);
                return true;

            case MessageType.PortInfo:
            {
                if (body.Length < 4) return false;
                var count = BinaryPrimitives.ReadInt32LittleEndian(body);
                if (count < 0 || count > body.Length - 4) return false;
                var slots = new int[count];
                for (var i = 0; i < count; i++) slots[i] = body[4 + i];
                request = new DsuRequest(type, clientId, slots, 0, 0, 0);
                return true;
            }

            case MessageType.PadData:
            {
                if (body.Length < 8) return false;
                ulong mac = 0;
                for (var i = 0; i < 6; i++) mac |= (ulong)body[2 + i] << (8 * i);
                request = new DsuRequest(type, clientId, [], body[0], body[1], mac);
                return true;
            }

            default:
                return false;
        }
    }

    private static bool CrcMatches(ReadOnlySpan<byte> packet)
    {
        var claimed = BinaryPrimitives.ReadUInt32LittleEndian(packet[8..]);

        Span<byte> copy = packet.Length <= 256 ? stackalloc byte[packet.Length] : new byte[packet.Length];
        packet.CopyTo(copy);
        BinaryPrimitives.WriteUInt32LittleEndian(copy[8..], 0);

        return DsuCrc32.Compute(copy) == claimed;
    }

    /// <summary>Builds a client request the way Dolphin does; used by tests and diagnostics.</summary>
    public static byte[] BuildPortInfoRequest(uint clientId, params int[] slots)
    {
        var packet = new byte[HeaderLength + 4 + 4 + Math.Max(slots.Length, 4)];
        ClientMagic.CopyTo(packet);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), (ushort)(packet.Length - HeaderLength));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), clientId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), (uint)MessageType.PortInfo);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(20), slots.Length);
        for (var i = 0; i < slots.Length; i++) packet[24 + i] = (byte)slots[i];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), DsuCrc32.Compute(packet));
        return packet;
    }

    /// <summary>Builds a data subscription the way Dolphin does; used by tests and diagnostics.</summary>
    public static byte[] BuildPadDataRequest(uint clientId, byte flags, byte slot, ulong mac)
    {
        var packet = new byte[HeaderLength + 4 + 8];
        ClientMagic.CopyTo(packet);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), (ushort)(packet.Length - HeaderLength));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), clientId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), (uint)MessageType.PadData);
        packet[20] = flags;
        packet[21] = slot;
        for (var i = 0; i < 6; i++) packet[22 + i] = (byte)(mac >> (8 * i));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), DsuCrc32.Compute(packet));
        return packet;
    }

    public static string DescribeMagic(ReadOnlySpan<byte> datagram) =>
        datagram.Length >= 4 ? Encoding.ASCII.GetString(datagram[..4]) : string.Empty;
}
