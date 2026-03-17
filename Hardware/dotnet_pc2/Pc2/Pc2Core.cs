using Beoported.Logging;
using Beoported.Masterlink;

namespace Beoported.Pc2;

/// <summary>
/// Core PC2 coordinator: initializes USB device, runs the event loop, routes messages.
/// </summary>
public sealed class Pc2Core : IDisposable
{
    public Pc2Device Device { get; }
    public Pc2Mixer Mixer { get; }
    public Beolink Beolink { get; }
    public AddressMask AddressMask { get; }

    /// <summary>
    /// Called on the background thread for every sent/received USB frame and event,
    /// formatted as an ANSI-colored string ready for display.
    /// </summary>
    public Action<string>? OnDebugMessage { get; set; }
    public Action<string>? OnStatusChanged { get; set; }

    /// <summary>
    /// Called on the background thread for every incoming Masterlink telegram.
    /// </summary>
    public Action<MasterlinkTelegram>? OnTelegram { get; set; }

    /// <summary>
    /// Called when the hardware confirms a mixer state change, with the new AudioSetup values.
    /// </summary>
    public Action<AudioSetup>? OnAudioSetupChanged { get; set; }

    /// <summary>
    /// Called when the "store" command is issued, with the current AudioSetup.
    /// The caller should persist this to disk/settings.
    /// </summary>
    public Action<AudioSetup>? OnStore { get; set; }

    /// <summary>Current audio parameters used when activating a source.</summary>
    public AudioSetup AudioSetup { get; private set; } = new();

    public Pc2Core(AddressMask addressMask = AddressMask.Promiscuous)
    {
        AddressMask = addressMask;
        Device = new Pc2Device();
        Mixer = new Pc2Mixer(Device);
        Beolink = new Beolink(Device);

        // Wire internal debug/telegram callbacks through to the public ones.
        Device.DebugLog = s => OnDebugMessage?.Invoke(s);
        Beolink.DebugLog = s => OnDebugMessage?.Invoke(s);
        Beolink.TelegramReceived = t => OnTelegram?.Invoke(t);
        Beolink.StartupSourceDetect = _ =>
        {
            switch (AudioSetup.DefaultSource)
            {
                case Pc2DefaultSource.PC:
                    RunCommand("pc", 0);
                    break;
                case Pc2DefaultSource.ML:
                    RunCommand("on", 0);
                    break;
            }
        };
        Beolink.RequestAudioBus = RequestAudioBus;

        // Wire Beo4 IR remote keys to the internal handler.
        Beolink.KeystrokeCallback = HandleBeo4Key;
    }

    /// <summary>Open USB device and send init sequence.</summary>
    public void Open()
    {
        Device.Open();
        Init();
        Device.SetAddressFilter(AddressMask);
    }

    /// <summary>Request the current audio bus state from the Masterlink network.</summary>
    public void RequestAudioBus()
    {
        Beolink.SendTelegram(DecodedTelegrams.RequestAudioBus());
    }

    /// <summary>Update the audio parameters used when activating a source.</summary>
    public void SetAudioSetup(AudioSetup setup) => AudioSetup = setup;

    private void Init()
    {
        Device.SendMessage([0xF1]);             // INIT
        Device.SendMessage([0x80, 0x01, 0x00]); // SET_NODE = 0x01
        Mixer.SetParameters(AudioSetup); // apply default audio setup (volume 0, neutral bass/treble/balance, loudness off)

    }

    /// <summary>
    /// Start the message loop on a background thread.
    /// Returns immediately; the loop runs until <paramref name="ct"/> is cancelled.
    /// </summary>
    public void Start(CancellationToken ct)
    {
        RequestAudioBus();
        var thread = new Thread(() => RunLoop(ct))
        {
            IsBackground = true,
            Name = "PC2-EventLoop"
        };
        thread.Start();
    }

    private void RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!Device.TryRead(out var msg, 500))
                continue;
            ProcessMessage(msg);
        }
    }

    /// <summary>Shutdown the PC2 device.</summary>
    public void Shutdown()
    {
        Device.SendMessage([0xA7]); // SHUTDOWN
    }

    // ── High-level command API ────────────────────────────────────────────────

    /// <summary>
    /// Execute a named command. Returns false if the command is not recognised.
    /// "help" / "?" are intentionally not handled here — the caller decides how to display help.
    /// </summary>
    public bool RunCommand(string cmd, int arg = 0)
    {
        cmd = cmd.ToLowerInvariant();

        // ── "pc" = local PC audio source (no Masterlink telegram) ────────
        if (cmd == "pc")
        {
            Mixer.TransmitLocally(true);
            Mixer.TransmitFromMl(false);
            Mixer.SpeakerPower(true);
        }
        else if (cmd == "on")
        {
            Mixer.TransmitLocally(false);
            Mixer.TransmitFromMl(true);
            Mixer.SpeakerPower(true);
            Mixer.SpeakerMute(false);
        }
        // ── Any other named Masterlink source ─────────────────────────────
        else if (SourceIdFromName(cmd) is byte mlSourceId)
        {
            GotoSource(mlSourceId, (byte)arg);
            Mixer.TransmitLocally(false);
            Mixer.TransmitFromMl(true);
            Mixer.SpeakerPower(true);
            Mixer.SpeakerMute(false);
        }
        else if (cmd is "vol+" or "vol++" or "v+")
        {
            if (Mixer.SpeakersMuted) Mixer.SpeakerMute(false);
            int steps = arg > 0 ? arg : 1;
            AudioSetup.Volume = (byte)Math.Clamp(AudioSetup.Volume + steps, 0, 63);
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd is "vol-" or "vol--" or "v-")
        {
            if (Mixer.SpeakersMuted) Mixer.SpeakerMute(false);
            int steps = arg > 0 ? arg : 1;
            AudioSetup.Volume = (byte)Math.Clamp(AudioSetup.Volume - steps, 0, 63);
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd is "bass+" or "bas+")
        {
            int steps = arg > 0 ? arg : 1;
            AudioSetup.Bass = (sbyte)Math.Clamp(AudioSetup.Bass + steps, -6, 6);
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd is "bass-" or "bas-")
        {
            int steps = arg > 0 ? arg : 1;
            AudioSetup.Bass = (sbyte)Math.Clamp(AudioSetup.Bass - steps, -6, 6);
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd is "treble+" or "tre+")
        {
            int steps = arg > 0 ? arg : 1;
            AudioSetup.Treble = (sbyte)Math.Clamp(AudioSetup.Treble + steps, -6, 6);
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd is "treble-" or "tre-")
        {
            int steps = arg > 0 ? arg : 1;
            AudioSetup.Treble = (sbyte)Math.Clamp(AudioSetup.Treble - steps, -6, 6);
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd is "balance+" or "bal+")
        {
            int steps = arg > 0 ? arg : 1;
            AudioSetup.Balance = (sbyte)Math.Clamp(AudioSetup.Balance + steps, -6, 6);
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd is "balance-" or "bal-")
        {
            int steps = arg > 0 ? arg : 1;
            AudioSetup.Balance = (sbyte)Math.Clamp(AudioSetup.Balance - steps, -6, 6);
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd is "loudness" or "loud")
        {
            AudioSetup.Loudness = !AudioSetup.Loudness;
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd is "loudness+" or "lou+" or "l+")
        {
            AudioSetup.Loudness = true;
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd is "loudness-" or "lou-" or "l-")
        {
            AudioSetup.Loudness = false;
            Mixer.SetParameters(AudioSetup);
        }
        else if (cmd == "mute")
        {
            bool muted = !Mixer.SpeakersMuted;
            Mixer.SpeakerMute(muted);
            /*
            if (muted)
            {
                Mixer.TransmitLocally(false);
                Mixer.TransmitFromMl(false);
                Mixer.SpeakerPower(false);
            }
            else
            {
                Mixer.SpeakerPower(true);
            }*/
        }
        else if (cmd is "off" or "standby")
        {
            Mixer.TransmitLocally(false);
            Mixer.TransmitFromMl(false);
            Mixer.SpeakerPower(false);
        }
        else if (cmd is "alloff" or "allstandby")
        {
            Beolink.SendTelegram(DecodedTelegrams.AllStandby());
            Mixer.TransmitLocally(false);
            Mixer.TransmitFromMl(false);
            Mixer.SpeakerPower(false);
        }
        else if (cmd == "store")
        {
            OnStore?.Invoke(AudioSetup);
            SetAudioSetup(AudioSetup);// ??

        }
        else if (KeyFromCommand(cmd) is Beo4Key key)
        {
            Beolink.SendTelegram(DecodedTelegrams.SendBeo4Key(0x00, key));
        }
        else
        {
            return false;
        }
        return true;
    }

    /// <summary>Send a GOTO_SOURCE telegram on the Masterlink bus.</summary>
    public void GotoSource(byte mlSourceId, byte trackOrPreset = 0)
    {
        var tgram = DecodedTelegrams.GotoSource(mlSourceId, trackOrPreset);
        Beolink.SendTelegram(tgram);
    }

    /// <summary>Map a lowercase source command name to its Masterlink source ID. Returns null if unknown.</summary>
    public static byte? SourceIdFromName(string name) => SourceNames.FromCommand(name);

    /// <summary>Map a lowercase command name to its Beo4Key. Returns null if unknown.</summary>
    public static Beo4Key? KeyFromCommand(string cmd) => cmd switch
    {
        "up" => Beo4Key.ArrowUp,
        "down" => Beo4Key.ArrowDown,
        "left" => Beo4Key.ArrowLeft,
        "right" => Beo4Key.ArrowRight,
        "go" => Beo4Key.Go,
        "play" => Beo4Key.Play,
        "stop" => Beo4Key.Stop,
        "record" => Beo4Key.Record,
        "menu" => Beo4Key.Menu,
        "exit" => Beo4Key.ArrowLeft,  // map exit → left (back)
        "return" => Beo4Key.ArrowLeft,
        "select" => Beo4Key.Go,
        "red" => Beo4Key.Red,
        "green" => Beo4Key.Green,
        "blue" => Beo4Key.Blue,
        "yellow" => Beo4Key.Yellow,
        "standby" => Beo4Key.Standby,
        "off" => Beo4Key.Standby,
        _ => null,
    };


    public void ProcessMessage(byte[] tgram)
    {
        if (tgram.Length < 3)
        {
            OnDebugMessage?.Invoke(ConsoleLog.FormatUnknownMessage(tgram, 0));
            return;
        }

        byte msgType = tgram[2];
        switch (msgType)
        {
            case 0x00: // Masterlink telegram
                Beolink.ProcessMlTelegram(tgram);
                MasterlinkTelegram mlt;
                try
                {
                    mlt = MasterlinkTelegram.Parse(tgram);
                    if (mlt.Type == MasterlinkTelegram.TelegramType.Status)
                    {
                        if (mlt.Payload.Length > 1)
                        {
                            string source = SourceNames.GetName(mlt.Payload[1]);
                            if (mlt.Payload.Length > 2 && mlt.Payload[2] > 0 && mlt.Payload[2] != 255)
                                source = $"Current source:{source} {mlt.Payload[2]}";
                            OnStatusChanged?.Invoke(source);
                        }
                    }
                }
                catch (Exception e) { }
                break;
            case 0x02: // Beo4 IR keycode
                if (tgram.Length > 6)
                    Beolink.ProcessBeo4Keycode(tgram[4], tgram[6]);

                OnDebugMessage?.Invoke("(Beo4 keycode: " + ((Beo4Key)tgram[6]));

                break;


            case 0x03: // Mixer state
                var r = Mixer.ProcessMixerState(tgram);
                if (r != null)
                {
                    OnDebugMessage?.Invoke($"Mixer state {BitConverter.ToString(tgram)}");
                    r.DefaultSource = AudioSetup.DefaultSource;
                    AudioSetup = r;
                    OnAudioSetupChanged?.Invoke(r);
                    OnDebugMessage?.Invoke($"Mixer state (hw): volume={r.Volume}, bass={r.Bass}, treble={r.Treble}, balance={r.Balance}, loudness={r.Loudness}");
                }
                break;
            case 0x06: // Headphone state
                Mixer.ProcessHeadphoneState(tgram);
                OnDebugMessage?.Invoke("Headphone state: ");
                break;

            default:
                OnDebugMessage?.Invoke(ConsoleLog.FormatUnknownMessage(tgram, msgType));
                break;
        }
    }

    private void HandleBeo4Key(Beo4Key keycode)
    {
        if (Beo4.TryGetSource(keycode, out var source))
        {
            GotoSource((byte)source);
            Mixer.TransmitLocally(true); // maybe should be false?
            Mixer.TransmitFromMl(true); // maybe should be false?
            Mixer.SpeakerPower(true);
        }
        else if (keycode == Beo4Key.Mute)
            Mixer.SpeakerMute(!Mixer.SpeakersMuted);
        else if (keycode == Beo4Key.VolUp)
        {
            if (Mixer.SpeakersMuted) Mixer.SpeakerMute(false);
            AudioSetup.Volume = (byte)Math.Clamp(AudioSetup.Volume + 1, 0, 63);
            Mixer.SetParameters(AudioSetup);
        }
        else if (keycode == Beo4Key.VolDown)
        {
            if (Mixer.SpeakersMuted) Mixer.SpeakerMute(false);
            AudioSetup.Volume = (byte)Math.Clamp(AudioSetup.Volume - 1, 0, 63);
            Mixer.SetParameters(AudioSetup);
        }
        else if (keycode == Beo4Key.Standby)
        {
            Mixer.TransmitLocally(false);
            Mixer.TransmitFromMl(false);
            Mixer.SpeakerPower(false);
        }
    }

    public void Dispose()
    {
        try { Mixer.TransmitLocally(false); } catch { /* best effort */ }
        try { Mixer.SpeakerPower(false); } catch { /* best effort */ }
        try { Shutdown(); } catch { /* best effort */ }
        Device.Dispose();
    }
}
