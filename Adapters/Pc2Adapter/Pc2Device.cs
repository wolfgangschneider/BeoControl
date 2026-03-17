using BeoControl.Interfaces;

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

    public bool IsConnected { get; private set; }
    public DeviceInfo Info { get; private set; } = new(DeviceType.PC2, "PC2", null);


    public event Action<StatusMessage>? OnStatusChanged;
    public event Action<LogMessage>? OnLog;

    /// <summary>Raised when the user issues a "store" command so the caller can persist audio settings.</summary>
    public event Action<AudioSetup>? OnStore;

    /// <summary>Raised when the current PC2 audio setup changes.</summary>
    public event Action<AudioSetup>? OnAudioSetupChanged;

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
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, FormatAudioStatus(setup), StatusKind.AudioSetup));
        };
        _core.OnStore = setup =>
        {
            OnStore?.Invoke(setup);
        };
        _core.OnStatusChanged = status => OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, status, StatusKind.Source));

    }

    public async Task Connect()
    {
        try
        {
            _core.Open();
            _cts = new CancellationTokenSource();
            _core.Start(_cts.Token);
            IsConnected = true;
            // OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, $"● PC2 connected  {FormatAudioStatus(_core.AudioSetup)}"));

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
        OnStatusChanged?.Invoke(new StatusMessage(StatusType.Idle, "○ PC2 disconnected", StatusKind.Connection));
    }

    public void SendCommand(string cmd, string? arg = null)
    {
        var n = arg is not null && int.TryParse(arg, out var parsed) ? parsed : 0;
        if (!_core.RunCommand(cmd, n))
        {
            OnLog?.Invoke(new LogMessage(LogLevel.Warning, $"PC2: unknown command '{cmd}'"));
            return;
        }

        if (!string.Equals(cmd, "store", StringComparison.OrdinalIgnoreCase) && !_core.RunCommand("store", 0))
            OnLog?.Invoke(new LogMessage(LogLevel.Warning, "PC2: automatic store command failed"));
    }

    private static string FormatAudioStatus(AudioSetup s) =>
        $"vol={s.Volume}  bass={s.Bass:+0;-0;0}  treble={s.Treble:+0;-0;0}  balance={s.Balance:+0;-0;0}  loudness={(s.Loudness ? "on" : "off")}";

    public void Dispose()
    {
        Disconnect();
        _core.Dispose();
    }
}
