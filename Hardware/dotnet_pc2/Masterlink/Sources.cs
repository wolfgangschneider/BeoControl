namespace Beoported.Masterlink;

/// <summary>Masterlink source IDs as used on the B&amp;O bus.</summary>
public enum Source : byte
{
    // Vidwo sources
    Tv       = 0x0B,
    VMem     = 0x15,
    VTape    = 0x15,
    VTape2   = 0x16,
    Dvd2     = 0x16,
    Dtv      = 0x1F,
    Sat      = 0x1F,
    Dvd      = 0x29,
    VAux     = 0x33,
    Dtv2     = 0x33,
    VAux2    = 0x3E,
    Doorcam  = 0x3E,
    Pc       = 0x47,
    // audio sources start at 0x60, but there are some exceptions below
    Radio    = 0x6F,
    AMem     = 0x79,
    ATape    = 0x79,
    AMem2    = 0x7A,
    Cd       = 0x8D,
    AAux     = 0x97,
    NRadio   = 0xA1,
    Phono    = 0xA1,
}

public static class SourceNames
{
    private static readonly Dictionary<byte, string> Names = new()
    {
        [0x0B] = "TV",
        [0x15] = "V_MEM",
        [0x16] = "DVD2",
        [0x1F] = "SAT",
        [0x29] = "DVD",
        [0x33] = "V.AUX",
        [0x3E] = "DOORCAM",
        [0x47] = "PC",
        [0x6F] = "RADIO",
        [0x79] = "A.MEM",
        [0x7A] = "A.MEM2",
        [0x8D] = "CD",
        [0x97] = "A.AUX",
        [0xA1] = "N.RADIO",
    };

    private static readonly Dictionary<string, byte> CommandMap = new()
    {
        // ── Video sources ──────────────────────────────────────────────────
        ["tv"]      = (byte)Source.Tv,
        ["vmem"]    = (byte)Source.VMem,
        ["vtape"]   = (byte)Source.VTape,     // Masterlink alias for VMem
        ["vtape2"]  = (byte)Source.VTape2,
        ["dvd2"]    = (byte)Source.Dvd2,
        ["dtv"]     = (byte)Source.Dtv,
        ["sat"]     = (byte)Source.Sat,       // Beo4 / Masterlink alias for Dtv
        ["dvd"]     = (byte)Source.Dvd,
        ["vaux"]    = (byte)Source.VAux,
        ["v.aux"]   = (byte)Source.VAux,      // Beo4-style name
        ["dtv2"]    = (byte)Source.Dtv2,
        ["vaux2"]   = (byte)Source.VAux2,
        ["doorcam"] = (byte)Source.Doorcam,
        // ── Audio sources ──────────────────────────────────────────────────
        ["radio"]   = (byte)Source.Radio,
        ["amem"]    = (byte)Source.AMem,
        ["tape"]    = (byte)Source.ATape,
        ["atape"]   = (byte)Source.ATape,     // Beo4-style name
        ["atape2"]  = (byte)Source.AMem2,     // Beo4-style name
        ["amem2"]   = (byte)Source.AMem2,
        ["cd"]      = (byte)Source.Cd,
        ["aaux"]    = (byte)Source.AAux,
        ["aux"]     = (byte)Source.AAux,
        ["a.aux"]   = (byte)Source.AAux,      // Beo4-style name
        ["nradio"]  = (byte)Source.NRadio,
        ["phono"]   = (byte)Source.Phono,     // Beo4-style name, same as nradio
    };

    public static string? CommandFromId(byte id) =>
        CommandMap.FirstOrDefault(kv => kv.Value == id).Key;

    public static string GetName(byte id) =>
        Names.TryGetValue(id, out var name) ? name : $"0x{id:X2}";

    public static byte? FromCommand(string cmd) =>
        CommandMap.TryGetValue(cmd.ToLowerInvariant(), out var id) ? id : null;

        /// <summary>
    /// Returns true if the source ID is a video source (routes to V_MASTER),
    /// false if it is an audio source (routes to A_MASTER).
    /// </summary>
    public static bool IsVideoSource(byte sourceId) => sourceId < 0x60;
}
