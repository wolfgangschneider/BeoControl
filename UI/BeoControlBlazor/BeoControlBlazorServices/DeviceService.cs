using Beo4Adapter;
using Beo4Adapter.Transport;

using BeoControl.Interfaces;

using BeoControlBlazor.Services;

using Microsoft.Extensions.Hosting;

using Pc2Adapter;

namespace BeoControlBlazorServices;

/// <summary>
/// Singleton service that owns the active B&amp;O device connection and persists settings.
/// Registered as both a singleton and an <see cref="IHostedService"/> so it auto-connects on startup.
/// </summary>
public class DeviceService : IHostedService, IDisposable
{
    private static readonly TimeSpan WindowsBluetoothAutostartDelay = TimeSpan.FromSeconds(4);
    private static readonly string StartupTracePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BeoControl", "startup-trace.log");

    private readonly object _autoConnectLock = new();
    private Task? _autoConnectTask;
    private IDevice? _device;

    public AppSettings Settings { get; } = AppSettings.Load();

    public bool IsConnected => _device?.IsConnected ?? false;
    public DeviceInfo? CurrentDevice => _device?.Info;
    public AudioSetupDto CurrentPc2AudioSetup => Settings.AudioSetup;
    public string? LastPc2AudioStatusText { get; private set; }

    public StatusMessage LastStatus { get; private set; } = new(StatusType.Idle, "Not connected", StatusKind.Connection);

    public event Action<StatusMessage>? OnStatusChanged;

    // ── IHostedService ────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken) =>
        await AutoConnectAsync();

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Disconnect(silent: true);
        return Task.CompletedTask;
    }

    // ── Connect helpers ───────────────────────────────────────────────────────

    public Task AutoConnectAsync()
    {
        lock (_autoConnectLock)
        {
            if (_autoConnectTask is { IsCompleted: false })
                return _autoConnectTask;

            _autoConnectTask = AutoConnectCoreAsync();
            return _autoConnectTask;
        }
    }

    private async Task AutoConnectCoreAsync()
    {
        TraceStartup($"AutoConnect start. LastDevice={Settings.LastDevice}, LastBluetoothId={Settings.LastBluetooth?.Id ?? "<null>"}, LastBluetoothName={Settings.LastBluetooth?.Name ?? "<null>"}, Silent={IsSilentLaunchRequested()}");
        switch (Settings.LastDevice)
        {
            case DeviceType.USB when Settings.LastSerial?.Id is { } port:
                TraceStartup($"AutoConnect using USB port {port}.");
                await ConnectSerialAsync(port);
                break;
            case DeviceType.BT when Settings.LastBluetooth?.Id is { } id:
                TraceStartup($"AutoConnect using Bluetooth id {id}.");
                await ConnectBluetoothAsync(id, delayForStartup: ShouldDelayBluetoothAutoConnect(), preferredDeviceName: Settings.LastBluetooth?.Name);
                break;
            case DeviceType.PC2:
                TraceStartup("AutoConnect using PC2.");
                await ConnectPc2Async();
                break;
            default:
                TraceStartup("AutoConnect skipped because no previous device was stored.");
                break;
        }
    }

    public async Task ConnectSerialAsync(string? portName = null)
    {
        Notify(StatusType.Working, portName is not null ? $"Connecting to {portName}…" : "Connecting to serial…");
        try
        {
            ReplaceDevice(null);
            var device = new Beo4Device(new SerialTransport(portName));
            await device.Connect();
            ReplaceDevice(device);
            PersistDevice();
            Notify(StatusType.Ok, $"Connected: {device.Info.Name ?? device.Info.Id}");
        }
        catch (Exception ex) { Notify(StatusType.Error, $"Serial failed: {ex.Message}"); }
    }

    public async Task ConnectBluetoothAsync(string? deviceId = null, bool delayForStartup = false, string? preferredDeviceName = null)
    {
        try
        {
            ReplaceDevice(null);
            if (delayForStartup)
            {
                TraceStartup($"Bluetooth auto-connect delaying for {WindowsBluetoothAutostartDelay.TotalSeconds:0.#} seconds.");
                Notify(StatusType.Working, "Waiting for Windows Bluetooth startup…");
                await Task.Delay(WindowsBluetoothAutostartDelay);
            }

            var preferScanFirst = ShouldPreferBluetoothScanFirst(delayForStartup);
            Exception? firstAttemptError = null;

            if (preferScanFirst)
            {
                TraceStartup($"Trying Bluetooth startup scan first. PreferredName={preferredDeviceName ?? "<null>"}.");
                firstAttemptError = await TryConnectBluetoothAsync(null, preferredDeviceName);
                if (firstAttemptError is null)
                {
                    TraceStartup("Bluetooth startup scan succeeded.");
                    return;
                }

                TraceStartup($"Bluetooth startup scan failed: {firstAttemptError.Message}");
                if (string.IsNullOrWhiteSpace(deviceId))
                    throw firstAttemptError;

                Notify(StatusType.Working, "Bluetooth scan failed, trying saved device id…");
                TraceStartup($"Trying direct Bluetooth fallback with id {deviceId}.");
                var directFallbackError = await TryConnectBluetoothAsync(deviceId, preferredDeviceName);
                if (directFallbackError is null)
                {
                    TraceStartup($"Bluetooth direct fallback succeeded for {deviceId}.");
                    return;
                }

                TraceStartup($"Bluetooth direct fallback failed for {deviceId}: {directFallbackError.Message}");
                throw new Exception("Bluetooth reconnect failed after scan-first startup and direct fallback.", directFallbackError);
            }

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                TraceStartup($"Trying direct Bluetooth reconnect with id {deviceId}.");
                firstAttemptError = await TryConnectBluetoothAsync(deviceId, preferredDeviceName);
                if (firstAttemptError is null)
                {
                    TraceStartup($"Bluetooth direct reconnect succeeded for {deviceId}.");
                    return;
                }

                TraceStartup($"Bluetooth direct reconnect failed for {deviceId}: {firstAttemptError.Message}");
                Notify(StatusType.Working, "Bluetooth direct reconnect failed, scanning…");
            }

            TraceStartup($"Trying Bluetooth scan fallback. PreferredName={preferredDeviceName ?? "<null>"}.");
            var scanFallbackError = await TryConnectBluetoothAsync(null, preferredDeviceName);
            if (scanFallbackError is null)
            {
                TraceStartup("Bluetooth scan fallback succeeded.");
                return;
            }

            TraceStartup($"Bluetooth scan fallback failed: {scanFallbackError.Message}");
            throw firstAttemptError is not null
                ? new Exception("Bluetooth reconnect failed after direct reconnect and scan fallback.", scanFallbackError)
                : scanFallbackError;
        }
        catch (Exception ex) when (LastStatus is not { Type: StatusType.Error, Kind: StatusKind.Connection })
        {
            TraceStartup($"Bluetooth connect failed: {ex.Message}");
            Notify(StatusType.Error, $"Bluetooth failed: {ex.Message}");
        }
    }

    public async Task ConnectPc2Async()
    {
        Notify(StatusType.Working, "Connecting to PC2…");
        try
        {
            ReplaceDevice(null);
            var device = new Pc2Device(Settings.ToAudioSetup());
            await device.Connect();
            ReplaceDevice(device);
            PersistDevice();
            Notify(StatusType.Ok, "Connected: PC2");
        }
        catch
        {
            ReplaceDevice(null);
            Notify(StatusType.Idle, "Disconnected");
        }
    }

    public async Task<bool> ScanPc2Async(Action<string>? progress = null, CancellationToken ct = default)
    {
        if (_device is Pc2Device { IsConnected: true })
        {
            progress?.Invoke("PC2 already connected.");
            return true;
        }

        ct.ThrowIfCancellationRequested();
        progress?.Invoke("Scanning PC2…");

        var probe = new Pc2Device(Settings.ToAudioSetup());
        try
        {
            await probe.Connect();
            var found = probe.IsConnected;
            progress?.Invoke(found ? "Found PC2 device." : "No PC2 device found.");
            return found;
        }
        finally
        {
            probe.Disconnect();
            probe.Dispose();
        }
    }

    // ── Scan helpers ──────────────────────────────────────────────────────────

    public async Task<List<DeviceInfo>> ScanSerialAsync(Action<string>? progress = null, CancellationToken ct = default)
    {
        using var t = new SerialTransport();
        return await t.ScanAsync(ct, s => { progress?.Invoke(s.Text); });
    }

    public async Task<List<DeviceInfo>> ScanBluetoothAsync(Action<string>? progress = null, CancellationToken ct = default)
    {
        using var t = new BluetoothTransport();
        return await t.ScanAsync(ct, s => { progress?.Invoke(s.Text); });
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public void SendCommand(string cmd, string? arg = null) => _device?.SendCommand(cmd, arg);

    public void UpdatePc2DefaultSource(Beoported.Pc2.Pc2DefaultSource defaultSource)
    {
        Settings.AudioSetup.DefaultSource = defaultSource;
        if (_device is Pc2Device pc2)
            pc2.CurrentAudioSetup.DefaultSource = defaultSource;
        Settings.Save();
        OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, LastStatus.Text, StatusKind.Info));
    }

    public void Disconnect(bool silent = false)
    {
        ReplaceDevice(null);
        if (!silent) Notify(StatusType.Idle, "Disconnected");
    }

    public void Dispose() => Disconnect(silent: true);

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<Exception?> TryConnectBluetoothAsync(string? deviceId, string? preferredDeviceName)
    {
        Notify(StatusType.Working, deviceId is not null ? $"Connecting to {deviceId}…" : "Scanning Bluetooth…");

        Beo4Device? candidate = null;
        try
        {
            candidate = new Beo4Device(new BluetoothTransport(deviceId, preferredDeviceName));
            await candidate.Connect();
            ReplaceDevice(candidate);
            PersistDevice();
            Notify(StatusType.Ok, $"Connected: {candidate.Info.Name ?? candidate.Info.Id}");
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            if (candidate is not null && !ReferenceEquals(_device, candidate))
                candidate.Dispose();
        }
    }

    private static bool ShouldDelayBluetoothAutoConnect()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        return IsSilentLaunchRequested();
    }

    private static bool ShouldPreferBluetoothScanFirst(bool delayForStartup) =>
        delayForStartup && OperatingSystem.IsWindows();

    private static bool IsSilentLaunchRequested()
    {
        return Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "/silent", StringComparison.OrdinalIgnoreCase));
    }

    private static void TraceStartup(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartupTracePath)!);
            File.AppendAllText(StartupTracePath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Startup trace write failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Startup trace write failed: {ex.Message}");
        }
    }

    private void ReplaceDevice(IDevice? next)
    {
        if (_device is not null)
        {
            _device.OnStatusChanged -= OnDeviceStatusChanged;
            _device.OnLog -= OnDeviceLog;
            if (_device is Pc2Device currentPc2)
            {
                currentPc2.OnStore -= OnPc2Store;
            }
            _device.Disconnect();
            _device.Dispose();
        }
        _device = next;
        LastPc2AudioStatusText = next is Pc2Device ? LastPc2AudioStatusText : null;
        if (_device is not null)
        {
            _device.OnStatusChanged += OnDeviceStatusChanged;
            _device.OnLog += OnDeviceLog;
            if (_device is Pc2Device nextPc2)
            {
                nextPc2.OnStore += OnPc2Store;
                SyncPc2AudioSetup(nextPc2.CurrentAudioSetup, save: false);
            }
        }
    }

    private void OnDeviceStatusChanged(StatusMessage msg)
    {
        if (msg.Kind == StatusKind.AudioSetup)
        {
            LastPc2AudioStatusText = msg.Text;
            if (Pc2AudioStatusParser.TryParse(msg.Text, out var parsedSetup))
            {
                parsedSetup.DefaultSource = Settings.AudioSetup.DefaultSource;
                SyncPc2AudioSetup(parsedSetup, save: true);
            }
        }
        else if (_device is not Pc2Device)
        {
            LastPc2AudioStatusText = null;
        }

        if (msg.Kind is not StatusKind.Source and not StatusKind.AudioSetup)
            LastStatus = msg;
        OnStatusChanged?.Invoke(msg);
    }

    private void OnDeviceLog(LogMessage msg) { /* future log panel */ }

    private void OnPc2Store(Beoported.Pc2.AudioSetup setup) =>
        SyncPc2AudioSetup(setup, save: true);

    private void PersistDevice()
    {
        if (_device is null) return;
        Settings.LastDevice = _device.Info.Type;
        if (_device.Info.Type == DeviceType.USB) Settings.LastSerial = _device.Info;
        else if (_device.Info.Type == DeviceType.BT) Settings.LastBluetooth = _device.Info;
        Settings.Save();
    }

    private void SyncPc2AudioSetup(Beoported.Pc2.AudioSetup setup, bool save)
    {
        Settings.UpdateAudioSetup(setup);
        if (save)
            Settings.Save();
    }

    private void Notify(StatusType type, string text, StatusKind kind = StatusKind.Connection)
    {
        LastStatus = new StatusMessage(type, text, kind);
        OnStatusChanged?.Invoke(LastStatus);
    }
}
