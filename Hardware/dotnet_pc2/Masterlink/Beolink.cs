using Beoported.Logging;
using Beoported.Pc2;

namespace Beoported.Masterlink;

/// <summary>
/// Masterlink bus communication: send/receive telegrams, handle requests, process IR keys.
/// </summary>
public class Beolink
{
    private byte lastSource = 0;
    private readonly Pc2Device _device;

    public Action<Beo4Key>? KeystrokeCallback { get; set; }
    public string LastSource { get; private set; } = string.Empty;

    /// <summary>Node addresses the PC2 answers requests for (Pc2Core adds the audio master's
    /// when the PC2 acts as one).</summary>
    public HashSet<byte> OwnNodes { get; } = [Nodes.Node01];

    /// <summary>Receives formatted debug text for every received ML frame and event.</summary>
    internal Action<string>? DebugLog { get; set; }

    /// <summary>Fired for every successfully parsed incoming Masterlink telegram.</summary>
    internal Action<MasterlinkTelegram>? TelegramReceived { get; set; }

    internal Action<string> StartupSourceDetect { get; set; }

    /// <summary>Called when the AudioBus state needs to be re-requested.</summary>
    internal Action? RequestAudioBus { get; set; }

    public Beolink(Pc2Device device)
    {
        _device = device;
    }

    /// <summary>Serialize and send a Masterlink telegram on the bus.</summary>
    public void SendTelegram(MasterlinkTelegram tgram)
    {
        var ml = tgram.Serialize();
        var msg = new byte[ml.Length + 1];
        msg[0] = 0xE0;
        Array.Copy(ml, 0, msg, 1, ml.Length);
        _device.SendMessage(msg);
        tgram.IsSent = true;
        TelegramReceived?.Invoke(tgram);
    }

    /// <summary>Process an incoming ML telegram from the PC2 USB frame.</summary>
    public void ProcessMlTelegram(byte[] frame)
    {
        if (frame.Length < 3)
        {
            DebugLog?.Invoke($"Unknown USB message: {BitConverter.ToString(frame)}");
            return;
        }
        MasterlinkTelegram mlt;
        try
        {
            mlt = MasterlinkTelegram.Parse(frame);
        }
        catch (ArgumentException ex)
        {
            DebugLog?.Invoke($"\x1b[1;31mParse error: {ex.Message}\x1b[0m");
            return;
        }
        DebugLog?.Invoke(ConsoleLog.FormatHexRow(frame, "Recv =>", isSend: false));
        if (mlt.AdditionalData.Length > 0)
            DebugLog?.Invoke($"\x1b[1;31m  Additional data: {BitConverter.ToString(mlt.AdditionalData)}\x1b[0m");

        TelegramReceived?.Invoke(mlt);

        switch (mlt.Type)
        {
            case MasterlinkTelegram.TelegramType.Request:
                HandleMlRequest(mlt);
                break;

            case MasterlinkTelegram.TelegramType.Command:
                HandleMlCommand(mlt);
                break;

            case MasterlinkTelegram.TelegramType.Status:
                switch (mlt.PldType)
                {
                    case MasterlinkTelegram.PayloadType.StatusInfo:
                        DebugLog?.Invoke($"Status info: source={SourceNames.GetName(mlt.Payload[1])}, track={mlt.Payload[2]}");
                        break;

                    case MasterlinkTelegram.PayloadType.TrackInfoLong:

                        // hack to get propper (more than once) trak info after source switch

                        if (mlt.Payload.Length > 2 && mlt.Payload[2] == 255)
                        {
                            byte newSource = mlt.Payload[1];
                            if (lastSource != newSource)
                            {
                                DebugLog?.Invoke($"Current source changed: {SourceNames.GetName(newSource)}");
                                Thread.Sleep(5000);
                                RequestAudioBus?.Invoke();
                                lastSource = newSource;
                            }
                        }
                        break;

                    case MasterlinkTelegram.PayloadType.TrackInfo:
                        DebugLog?.Invoke($"Track info: source={SourceNames.GetName(mlt.Payload[1])}, track={mlt.Payload[2]}");
                        // rport the leafing source
                        /*
                          byte newSource = mlt.Payload[1];
                     
                           if (lastSource != newSource)
                            {
                                DebugLog?.Invoke($"Current source changed: {SourceNames.GetName(newSource)}");
                                //Thread.Sleep(5000);
                                RequestAudioBus?.Invoke();
                               // lastSource = newSource;
                            }
                            */
                        break;

                    case MasterlinkTelegram.PayloadType.AudioBus:
                        if (mlt.Payload.Length > 3)
                        {
                            if (string.IsNullOrEmpty(LastSource))
                            {
                                LastSource = SourceNames.GetName(mlt.Payload[3]);
                                StartupSourceDetect?.Invoke(LastSource);
                            }
                            else // get track info for new source
                            {
                                var tgram = DecodedTelegrams.GotoSource(mlt.Payload[3], 0);
                                SendTelegram(tgram);

                            }
                            lastSource = mlt.Payload[3];

                        }
                        break;

                    case MasterlinkTelegram.PayloadType.MasterPresent:
                        DebugLog?.Invoke($"Master present: version {mlt.Payload[1]}");
                        break;

                    case MasterlinkTelegram.PayloadType.GotoSource:
                        DebugLog?.Invoke($"Goto source: {SourceNames.GetName(mlt.Payload[1])}");
                        break;

                    default:
                        DebugLog?.Invoke($"Unknown ML status payload: {mlt.PldType} {BitConverter.ToString(mlt.Payload)}");
                        break;
                }
                break;

            case MasterlinkTelegram.TelegramType.Info:
                DebugLog?.Invoke($"ML info: {mlt.PldType} {BitConverter.ToString(mlt.Payload)}");
                break;

            default:
                DebugLog?.Invoke($"Unknown ML telegram type: {mlt.Type} {BitConverter.ToString(mlt.Payload)}");

                break;
        }
    }

    /// <summary>Handle incoming Masterlink requests (MASTER_PRESENT, GOTO_SOURCE).</summary>
    private void HandleMlRequest(MasterlinkTelegram mlt)
    {
        // In promiscuous mode the PC2 also sees requests to other nodes; answering them
        // impersonates that node (seen: a MASTER_PRESENT reply as 0xC2, a source center).
        if (!OwnNodes.Contains(mlt.DestNode))
            return;

        if (mlt.PldType == MasterlinkTelegram.PayloadType.MasterPresent)
        {
            var reply = DecodedTelegrams.MasterPresentReply(mlt);
            SendTelegram(reply);
        }
        else if (mlt.PldType == MasterlinkTelegram.PayloadType.GotoSource)
        {
            if (mlt.Type == MasterlinkTelegram.TelegramType.Request &&
                mlt.Payload.Length == 7 && mlt.PayloadVersion == 1)
            {
                byte requestedSource = mlt.Payload[1];

                var statusReply = DecodedTelegrams.StatusInfo(requestedSource);
                statusReply.SrcNode = mlt.DestNode;
                SendTelegram(statusReply);

                var trackReply = DecodedTelegrams.TrackInfo(requestedSource);
                trackReply.SrcNode = mlt.DestNode;
                trackReply.DestNode = mlt.SrcNode;
                SendTelegram(trackReply);
            }
        }
    }

    /// <summary>
    /// Handle Beo4 keys sent to the PC2 over Masterlink, so it can be remote controlled without an
    /// IR eye: BEO4_KEY (payload [source, key]) and VIRTUAL_BEO4 as a MasterLink Gateway sends it
    /// (payload version 2: [0x00, dest, sec_source, link, key], e.g. Home Assistant's mlgw integration).
    /// </summary>
    private void HandleMlCommand(MasterlinkTelegram mlt)
    {
        if (mlt.DestNode != Nodes.Node01)
            return;

        byte? key = mlt.PldType switch
        {
            MasterlinkTelegram.PayloadType.Beo4Key when mlt.Payload.Length >= 2 => mlt.Payload[1],
            MasterlinkTelegram.PayloadType.VirtualBeo4 when mlt.PayloadVersion == 2 && mlt.Payload.Length >= 5 => mlt.Payload[4],
            _ => null,
        };
        if (key is null)
            return;

        DebugLog?.Invoke($"ML Beo4 key from {Nodes.GetName(mlt.SrcNode)}: {(Beo4Key)key.Value}");
        KeystrokeCallback?.Invoke((Beo4Key)key.Value);
    }

    /// <summary>Process an incoming Beo4 IR keycode.</summary>
    public void ProcessBeo4Keycode(byte type, byte keycode)
    {
        if (KeystrokeCallback != null)
            KeystrokeCallback((Beo4Key)keycode);
    }
}
