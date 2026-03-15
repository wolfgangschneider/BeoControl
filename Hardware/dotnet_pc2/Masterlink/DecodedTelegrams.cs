namespace Beoported.Masterlink;

/// <summary>Decoded telegram types with semantic meaning.</summary>
public static class DecodedTelegrams
{
    

    /// <summary>Build a GOTO_SOURCE request telegram.</summary>
    public static MasterlinkTelegram GotoSource(byte sourceId, byte trackOrPreset = 0)
    {
       // var master = IsVideoSource(sourceId) ? Nodes.VMaster : Nodes.AMaster;
        return new MasterlinkTelegram
        {
            DestNode =  Nodes.AMaster,
            SrcNode  = Nodes.Node01,
            DestSrc  = 0x00,
            SrcSrc   = 0x00,
            Type     = MasterlinkTelegram.TelegramType.Request,
            PldType  = MasterlinkTelegram.PayloadType.GotoSource,
            PayloadVersion = 1,
            Payload  = [0x02, sourceId, trackOrPreset, 0x02, 0x01, 0x00, 0x00],
        };
    }

    /// <summary>Build a MASTER_PRESENT reply from a request.</summary>
    public static MasterlinkTelegram MasterPresentReply(MasterlinkTelegram request)
    {
        return new MasterlinkTelegram
        {
            DestNode = request.SrcNode,
            SrcNode  = request.DestNode,
            Type     = MasterlinkTelegram.TelegramType.Status,
            PldType  = MasterlinkTelegram.PayloadType.MasterPresent,
            PayloadVersion = 4,
            Payload  = [0x01, 0x01, 0x01],
        };
    }

    /// <summary>Build a STATUS_INFO status telegram for a given source.</summary>
    public static MasterlinkTelegram StatusInfo(byte sourceId)
    {
        return new MasterlinkTelegram
        {
            DestNode = Nodes.AllAlt,
            SrcNode  = 0x00,
            Type     = MasterlinkTelegram.TelegramType.Status,
            PldType  = MasterlinkTelegram.PayloadType.StatusInfo,
            PayloadVersion = 4,
            Payload  =
            [
                sourceId, 0x01, 0x00, 0x00, 0x1F, 0xBE, 0x01, 0x00,
                0x00, 0x00, 0xFF, 0x02, 0x01, 0x00, 0x03, 0x01,
                0x01, 0x01, 0x03, 0x00, 0x02, 0x00, 0x00, 0x00,
                0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
            ],
        };
    }

    /// <summary>Build a TRACK_INFO status telegram for a given source.</summary>
    public static MasterlinkTelegram TrackInfo(byte sourceId)
    {
        return new MasterlinkTelegram
        {
            Type    = MasterlinkTelegram.TelegramType.Status,
            PldType = MasterlinkTelegram.PayloadType.TrackInfo,
            PayloadVersion = 5,
            Payload = [0x02, sourceId, 0x00, 0x02, 0x01, 0x00, 0x00, 0x00],
        };
    }

    /// <summary>Build a REQUEST AUDIO_BUS telegram to query the current audio bus state.</summary>
    public static MasterlinkTelegram RequestAudioBus()
    {
        return new MasterlinkTelegram
        {
            DestNode = 0xC1,
            SrcNode  = Nodes.Node01,
            DestSrc  = 0x00,
            SrcSrc   = 0x00,
            Type     = MasterlinkTelegram.TelegramType.Request,
            PldType  = MasterlinkTelegram.PayloadType.AudioBus,
            PayloadVersion = 1,
            Payload  = [],
        };
    }

    /// <summary>Build a RELEASE broadcast to all nodes (triggers standby on all Masterlink devices).</summary>
    public static MasterlinkTelegram AllStandby()
    {
        return new MasterlinkTelegram
        {
            DestNode = Nodes.All,
            SrcNode  = Nodes.Node01,
            DestSrc  = 0x00,
            SrcSrc   = 0x00,
            Type     = MasterlinkTelegram.TelegramType.Command,
            PldType  = MasterlinkTelegram.PayloadType.Release,
            PayloadVersion = 1,
            Payload  = [],
        };
    }
}
