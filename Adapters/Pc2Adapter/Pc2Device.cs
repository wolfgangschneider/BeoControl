using BeoControl.Interfaces;

using Beoported.Masterlink;
using Beoported.Pc2;

namespace Pc2Adapter;

/// <summary>
/// Wraps <see cref="Pc2Core"/> as an <see cref="IDevice"/>,
/// bridging the PC2 USB/Masterlink controller into the unified TUI.
/// </summary>
public sealed class Pc2Device : IDevice
{
    private readonly Pc2Core _core;
    private CancellationTokenSource? _cts;
    private SourceStatusMessage? _lastMlSource;   // last source reported from Masterlink

    public bool IsConnected { get; private set; }
    public DeviceInfo Info { get; private set; } = new(DeviceType.PC2, "PC2", null);


    public event Action<StatusMessage>? OnStatusChanged;
    public event Action<LogMessage>? OnLog;

    /// <summary>Raised when the user issues a "store" command so the caller can persist audio settings.</summary>
    public event Action<AudioSetup>? OnStore;

    /// <summary>Raised when the current PC2 audio setup changes.</summary>
    public event Action<AudioSetup>? OnAudioSetupChanged;

    /// <summary>Current speaker and audio routing state of the PC2.</summary>
    public Pc2RoutingState Routing => new(
        _core.Mixer.SpeakersOn,
        _core.Mixer.SpeakersMuted,
        _core.Mixer.TransmittingFromMl,
        _core.Mixer.TransmittingLocally);

    /// <summary>Current audio parameters (vol/bass/treble/balance/loudness).</summary>
    public AudioSetup CurrentAudioSetup => _core.AudioSetup;

    public Pc2Device(AudioSetup? initialAudioSetup = null)
    {
        _core = new Pc2Core();
        if (initialAudioSetup is not null)
            _core.SetAudioSetup(initialAudioSetup);

        _core.OnDebugMessage = msg => OnLog?.Invoke(new LogMessage(LogLevel.Debug, msg));
        _core.OnAudioSetupChanged = setup =>
        {
            OnAudioSetupChanged?.Invoke(setup);
            OnStatusChanged?.Invoke(CreateAudioSetupMessage(setup));
        };
        _core.OnStore = setup =>
        {
            OnStore?.Invoke(setup);
        };
        _core.OnSourceChanged = (sourceId, channel) =>
        {
            var command = SourceNames.CommandFromId(sourceId);
            if (string.IsNullOrWhiteSpace(command))
            {
                OnLog?.Invoke(new LogMessage(LogLevel.Debug, $"PC2: unknown source id 0x{sourceId:X2}"));
                return;
            }

            // Remember it even while the local input plays, so "on" can show it again.
            _lastMlSource = new SourceStatusMessage(command, channel);
            if (_core.Mixer.TransmittingLocally && !_core.Mixer.TransmittingFromMl)
                return;

            OnStatusChanged?.Invoke(_lastMlSource);
        };

    }

    public async Task Connect()
    {
        try
        {
            _core.Open();
            _cts = new CancellationTokenSource();
            _core.Start(_cts.Token);
            IsConnected = true;
            OnStatusChanged?.Invoke(new DeviceStatusMessage(
                DeviceStatus.Connected,
                $"● PC2 connected  {CreateAudioSetupMessage(_core.AudioSetup).Text}"));

        }
        catch (Exception ex)
        {
            IsConnected = false;
            OnLog?.Invoke(new LogMessage(LogLevel.Error, $"PC2 connect failed: {ex.Message}"));

        }
    }

    public void CancelAutoDetect() { /* PC2 connects synchronously, nothing to cancel */ }

    public void Disconnect()
    {
        _cts?.Cancel();
        _cts = null;
        try { _core.Shutdown(); } catch { }
        IsConnected = false;
        OnStatusChanged?.Invoke(new DeviceStatusMessage(DeviceStatus.Disconnected, "○ PC2 disconnected"));
    }

    public void SendCommand(string cmd, string? arg = null)
    {
        var n = arg is not null && int.TryParse(arg, out var parsed) ? parsed : 0;
        if (!_core.RunCommand(cmd, n))
        {
            OnLog?.Invoke(new LogMessage(LogLevel.Warning, $"PC2: unknown command '{cmd}'"));
            return;
        }

        if (string.Equals(cmd, "pc", StringComparison.OrdinalIgnoreCase))
            OnStatusChanged?.Invoke(new SourceStatusMessage("pc"));
        // Back from the local input: the Masterlink source doesn't report itself again by itself.
        else if (string.Equals(cmd, "on", StringComparison.OrdinalIgnoreCase) && _lastMlSource is not null)
            OnStatusChanged?.Invoke(_lastMlSource);

        if (!string.Equals(cmd, "store", StringComparison.OrdinalIgnoreCase) && !_core.RunCommand("store", 0))
            OnLog?.Invoke(new LogMessage(LogLevel.Warning, "PC2: automatic store command failed"));
    }

    private static AudioSetupMessage CreateAudioSetupMessage(AudioSetup setup) =>
        new(setup.Volume, setup.Bass, setup.Treble, setup.Balance, setup.Loudness);

    public void Dispose()
    {
        Disconnect();
        _core.Dispose();
    }
}

/// <summary>PC2 speaker and routing state: what the speakers play (Masterlink and/or the local input).</summary>
public sealed record Pc2RoutingState(bool SpeakersOn, bool Muted, bool FromMasterlink, bool Local);
