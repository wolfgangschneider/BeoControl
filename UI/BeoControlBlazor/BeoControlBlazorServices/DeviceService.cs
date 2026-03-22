using Beo4Adapter;
using Beo4Adapter.Transport;

using BeoControl.Interfaces;

using BeoControlBlazor.Services;

using Pc2Adapter;

namespace BeoControlBlazorServices;

/// <summary>
/// Singleton service that owns the active B&amp;O device connection and persists settings.
/// App hosts trigger startup/shutdown explicitly from their own lifecycle hooks.
/// </summary>
public class DeviceService : IDisposable
{
    private readonly record struct BluetoothConnectPolicy(TimeSpan StartupDelay, int AttemptCount, TimeSpan RetryDelay)
    {
        public static BluetoothConnectPolicy Manual => new(TimeSpan.Zero, 1, TimeSpan.Zero);
        public static BluetoothConnectPolicy WindowsSilentAutoStart => new(TimeSpan.FromSeconds(4), 1, TimeSpan.Zero);
        public static BluetoothConnectPolicy MacAutoStart => new(TimeSpan.FromSeconds(2), 3, TimeSpan.FromSeconds(2));
        public bool DelayBeforeFirstAttempt => StartupDelay > TimeSpan.Zero;
    }

    private readonly object _autoConnectLock = new();
    private Task? _autoConnectTask;
    private IDevice? _device;

    public AppSettings Settings { get; } = AppSettings.Load();
    public bool IsConnected => _device?.IsConnected ?? false;
    public DeviceInfo? CurrentDevice => _device?.Info;
    public AudioSetupDto CurrentPc2AudioSetup => Settings.AudioSetup;

    public event Action<StatusMessage>? OnStatusChanged;

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
        switch (Settings.LastDevice)
        {
            case DeviceType.USB when Settings.LastSerial?.Id is { } port:
                await ConnectSerialAsync(port);
                break;
            case DeviceType.BT when Settings.LastBluetooth?.Id is { } id:
                await ConnectBluetoothAsync(id, preferredDeviceName: Settings.LastBluetooth?.Name, policy: GetBluetoothAutoConnectPolicy());
                break;
            case DeviceType.PC2:
                await ConnectPc2Async();
                break;
            default:
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

    public Task ConnectBluetoothAsync(string? deviceId = null, string? preferredDeviceName = null)
    {
        return ConnectBluetoothAsync(deviceId, preferredDeviceName, BluetoothConnectPolicy.Manual);
    }

    private async Task ConnectBluetoothAsync(string? deviceId, string? preferredDeviceName, BluetoothConnectPolicy policy)
    {
        try
        {
            ReplaceDevice(null);
            if (policy.DelayBeforeFirstAttempt)
            {
                Notify(StatusType.Working, OperatingSystem.IsMacCatalyst()
                    ? "Waiting for Apple Bluetooth startup…"
                    : "Waiting for Windows Bluetooth startup…");
                await Task.Delay(policy.StartupDelay);
            }

            Exception? connectError = null;
            for (var attempt = 1; attempt <= policy.AttemptCount; attempt++)
            {
                if (attempt > 1)
                {
                    Notify(StatusType.Working, "Retrying Bluetooth reconnect…");
                    await Task.Delay(policy.RetryDelay);
                }

                connectError = await ConnectBluetoothAttemptAsync(deviceId, preferredDeviceName);
                if (connectError is null)
                    return;
            }

            throw connectError ?? new Exception("Bluetooth reconnect failed.");
        }
        catch (Exception ex)
        {
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
        OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, "PC2 default source updated.", StatusKind.Info));
    }

    public void Disconnect(bool silent = false)
    {
        ReplaceDevice(null);
        if (!silent) Notify(StatusType.Idle, "Disconnected");
    }

    public void Dispose() => Disconnect(silent: true);

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<Exception?> ConnectBluetoothAttemptAsync(string? deviceId, string? preferredDeviceName)
    {
        Exception? firstAttemptError = null;

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            firstAttemptError = await TryConnectBluetoothAsync(deviceId, preferredDeviceName);
            if (firstAttemptError is null)
                return null;

            Notify(StatusType.Working, "Bluetooth direct reconnect failed, scanning…");
        }

        var scanFallbackError = await TryConnectBluetoothAsync(null, preferredDeviceName);
        if (scanFallbackError is null)
            return null;

        return firstAttemptError is not null
            ? new Exception("Bluetooth reconnect failed after direct reconnect and scan fallback.", scanFallbackError)
            : scanFallbackError;
    }

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

    private static BluetoothConnectPolicy GetBluetoothAutoConnectPolicy()
    {
        if (OperatingSystem.IsMacCatalyst())
            return BluetoothConnectPolicy.MacAutoStart;

        if (OperatingSystem.IsWindows() && IsSilentLaunchRequested())
            return BluetoothConnectPolicy.WindowsSilentAutoStart;

        return BluetoothConnectPolicy.Manual;
    }

    private static bool IsSilentLaunchRequested()
    {
        return Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "/silent", StringComparison.OrdinalIgnoreCase));
    }

    private void ReplaceDevice(IDevice? next)
    {
        if (_device is not null)
        {
            _device.OnStatusChanged -= OnDeviceStatusChanged;
            if (_device is Pc2Device currentPc2)
            {
                currentPc2.OnStore -= OnPc2Store;
            }
            _device.Disconnect();
            _device.Dispose();
        }
        _device = next;
        if (_device is not null)
        {
            _device.OnStatusChanged += OnDeviceStatusChanged;
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
            if (Pc2AudioStatusParser.TryParse(msg.Text, out var parsedSetup))
            {
                parsedSetup.DefaultSource = Settings.AudioSetup.DefaultSource;
                SyncPc2AudioSetup(parsedSetup, save: true);
            }
        }

        OnStatusChanged?.Invoke(msg);
    }

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
        OnStatusChanged?.Invoke(new StatusMessage(type, text, kind));
    }
}
