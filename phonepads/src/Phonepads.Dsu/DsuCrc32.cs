namespace Phonepads.Dsu;

/// <summary>
/// Plain CRC-32 (IEEE 802.3, reflected, polynomial 0xEDB88320) — what the DSU protocol
/// stamps every packet with. Small enough to keep in-house rather than pull a package in.
/// </summary>
public static class DsuCrc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var entry = i;
            for (var bit = 0; bit < 8; bit++)
                entry = (entry & 1) != 0 ? 0xEDB88320u ^ (entry >> 1) : entry >> 1;
            table[i] = entry;
        }

        return table;
    }
}
