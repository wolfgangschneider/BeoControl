namespace Beoported.Masterlink;

/// <summary>Masterlink node addresses.</summary>
public static class Nodes
{
    public const byte All       = 0x80;
    public const byte AllAlt    = 0x83;
    public const byte AllBcast  = 0xFF;
    public const byte VMaster   = 0xC0;
    public const byte AMaster   = 0xC1;
    public const byte Pc1       = 0xC2;
    public const byte Node01    = 0x01;

    private static readonly Dictionary<byte, string> Names = new()
    {
        [0x80] = "ALL",
        [0x83] = "ALL",
        [0xFF] = "ALL_BCAST",
        [0xC0] = "V_MASTER",
        [0xC1] = "A_MASTER",
        [0xC2] = "PC_1",
        [0x01] = "NODE_01",
        [0x02] = "NODE_02",
        [0x03] = "NODE_03",
        [0x04] = "NODE_04",
        [0x05] = "NODE_05",
        [0x06] = "NODE_06",
        [0x07] = "NODE_07",
        [0x08] = "NODE_08",
        [0x09] = "NODE_09",
        [0x0A] = "NODE_0A",
        [0x0B] = "NODE_0B",
        [0x0C] = "NODE_0C",
        [0x0D] = "NODE_0D",
        [0x0E] = "NODE_0E",
        [0x0F] = "NODE_0F",
        [0x10] = "NODE_10",
        [0x11] = "NODE_11",
        [0x12] = "NODE_12",
        [0x13] = "NODE_13",
    };

    public static string GetName(byte id) =>
        Names.TryGetValue(id, out var name) ? name : $"NODE_{id:X2}";
}
