namespace Beoported.Masterlink;

/// <summary>
/// Represents a Masterlink telegram with header fields, payload, and serialization.
/// Wire format (inside PC2 USB frame):
///   [E0] [dest_node] [src_node] [01/SOT] [telegram_type] [dest_src] [src_src]
///   [00/spare] [payload_type] [payload_size] [payload_version] [payload...] [checksum] [00/EOT]
/// </summary>
public class MasterlinkTelegram
{
    // --- Enums ---

    public enum PayloadType : byte
    {
        MasterPresent    = 0x04,
        DisplayData      = 0x06,
        AudioBus         = 0x08,
        Metadata         = 0x0B,
        Beo4Key          = 0x0D,
        Standby          = 0x10,
        Release          = 0x11,
        Timer            = 0x3C,
        Clock            = 0x40,
        TrackInfo        = 0x44,
        GotoSource       = 0x45,
        DistributionReq  = 0x6C,
        TrackInfoLong    = 0x82,
        StatusInfo       = 0x87,
        DvdStatusInfo    = 0x94,
        PcPresent        = 0x96,
    }

    public enum TelegramType : byte
    {
        Command = 0x0A,
        Request = 0x0B,
        Status  = 0x14,
        Info    = 0x2C,
        Time    = 0x40,
        Config  = 0x5E,
    }

    // --- Name lookups ---

    public static readonly Dictionary<byte, string> PayloadTypeNames = new()
    {
        [0x04] = "MASTER_PRESENT",
        [0x05] = "???",
        [0x06] = "DISPLAY_DATA",
        [0x08] = "AUDIO_BUS",
        [0x0B] = "METADATA",
        [0x0D] = "BEO4_KEY",
        [0x10] = "STANDBY",
        [0x11] = "RELEASE",
        [0x12] = "???",
        [0x20] = "???",
        [0x30] = "???",
        [0x3C] = "TIMER",
        [0x40] = "CLOCK",
        [0x44] = "TRACK_INFO",
        [0x45] = "GOTO_SOURCE",
        [0x5C] = "???",
        [0x6C] = "DISTRIBUTION_REQUEST",
        [0x82] = "TRACK_INFO_LONG",
        [0x87] = "STATUS_INFO",
        [0x94] = "DVD_STATUS_INFO",
        [0x96] = "PC_PRESENT",
    };

    public static readonly Dictionary<byte, string> TelegramTypeNames = new()
    {
        [0x0A] = "COMMAND",
        [0x0B] = "REQUEST",
        [0x14] = "STATUS",
        [0x2C] = "INFO",
        [0x40] = "TIME",
        [0x5E] = "CONFIG",
    };

    // --- Fields ---

    public byte DestNode { get; set; }
    public byte SrcNode { get; set; }
    public byte DestSrc { get; set; }
    public byte SrcSrc { get; set; }
    public TelegramType Type { get; set; }
    public PayloadType PldType { get; set; }
    public int PayloadSize { get; set; }
    public int PayloadVersion { get; set; }
    public byte[] Payload { get; set; } = [];

    /// <summary>True if this telegram was sent by us; false if received.</summary>
    public bool IsSent { get; set; }

    /// <summary>Extra bytes found after the declared payload (before checksum/EOT), if any.</summary>
    public byte[] AdditionalData { get; set; } = [];

    // --- Parsing ---

    /// <summary>Minimum valid frame: 0x60 + len + msg_type + 10 ML header bytes + 0x61 = 14 bytes.</summary>
    private const int MinFrameLength = 14;

    /// <summary>
    /// Parse a MasterlinkTelegram from a complete PC2 USB frame (0x60 ... 0x61).
    /// Throws <see cref="ArgumentException"/> if the frame is too short.
    /// Sets <see cref="AdditionalData"/> if the frame contains bytes beyond the declared payload.
    /// </summary>
    public static MasterlinkTelegram Parse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < MinFrameLength)
            throw new ArgumentException(
                $"Frame too short ({frame.Length} bytes, need ≥{MinFrameLength}): {BitConverter.ToString(frame.ToArray())}");

        var data = frame[2..^1]; // strip 0x60, length byte, and trailing 0x61

        var tgram = new MasterlinkTelegram
        {
            DestNode       = data[1],
            SrcNode        = data[2],
            // data[3] = SOT (0x01)
            Type           = (TelegramType)data[4],
            SrcSrc         = data[6],
            DestSrc        = data[7],
            PldType        = (PayloadType)data[8],
            PayloadSize    = data[9],
            PayloadVersion = data[10],
        };

        // Payload starts at data[11], length = PayloadSize
        int payloadStart = 11;
        if (payloadStart + tgram.PayloadSize <= data.Length)
            tgram.Payload = data.Slice(payloadStart, tgram.PayloadSize).ToArray();
        else
            tgram.Payload = data[payloadStart..].ToArray();

        // Bytes after payload (before checksum+EOT = last 2 bytes) are unexpected extra data
        int expectedEnd = payloadStart + tgram.PayloadSize;
        if (expectedEnd + 2 < data.Length)
            tgram.AdditionalData = data.Slice(expectedEnd, data.Length - expectedEnd - 2).ToArray();

        return tgram;
    }

    // --- Serialization ---

    /// <summary>
    /// Serialize to the ML frame bytes (without the 0xE0 PC2 command prefix).
    /// Returns: [dest] [src] [SOT] [type] [dest_src] [src_src] [spare] [pld_type]
    ///          [pld_size] [pld_ver] [payload...] [checksum] [EOT]
    /// </summary>
    public byte[] Serialize()
    {
        var data = new List<byte>
        {
            DestNode,
            SrcNode,
            0x01, // SOT
            (byte)Type,
            DestSrc,
            SrcSrc,
            0x00, // spare
            (byte)PldType,
            (byte)Payload.Length,
            (byte)PayloadVersion,
        };
        data.AddRange(Payload);

        byte checksum = 0;
        foreach (var b in data)
            checksum += b;
        data.Add(checksum);

        data.Add(0x00); // EOT
        return data.ToArray();
    }

    // --- Display helpers ---

    public string PayloadTypeName =>
        PayloadTypeNames.TryGetValue((byte)PldType, out var n) ? n : $"0x{(byte)PldType:X2}";

    public string TelegramTypeName =>
        TelegramTypeNames.TryGetValue((byte)Type, out var n) ? n : $"0x{(byte)Type:X2}";

    public string SrcNodeName => Nodes.GetName(SrcNode);
    public string DestNodeName => Nodes.GetName(DestNode);
}
