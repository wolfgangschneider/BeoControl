using BeoControl.Interfaces;
using Beo4Adapter;
using Beo4Adapter.Transport;
using Pc2Adapter;

namespace BeoControlBlazor.Services;

/// <summary>
/// Singleton service that owns the active B&amp;O device connection and persists settings.
/// Registered as both a singleton and an <see cref="IHostedService"/> so it auto-connects on startup.
/// </summary>
public class DeviceService : IHostedService, IDisposable
{
    private IDevice? _device;

    public AppSettings Settings { get; } = AppSettings.Load();

    public bool IsConnected => _device?.IsConnected ?? false;
    public DeviceInfo? CurrentDevice => _device?.Info;
    public StatusType StatusType { get; private set; } = StatusType.Idle;
    public string StatusText { get; private set; } = "Not connected";

    public event Action? StateChanged;

    // ── IHostedService ────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken) =>
        await AutoConnectAsync();

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Disconnect(silent: true);
        return Task.CompletedTask;
    }

    // ── Connect helpers ───────────────────────────────────────────────────────

    public async Task AutoConnectAsync()
    {
        switch (Settings.LastDevice)
        {
            case DeviceType.Serial when Settings.LastSerial?.Id is { } port:
                await ConnectSerialAsync(port);
                break;
            case DeviceType.BT when Settings.LastBluetooth?.Id is { } id:
                await ConnectBluetoothAsync(id);
                break;
            case DeviceType.PC2:
                await ConnectPc2Async();
                break;
        }
    }

    public async Task ConnectSerialAsync(string? portName = null)
    {
        SetStatus(StatusType.Working, portName is not null ? $"Connecting to {portName}…" : "Connecting to serial…");
        try
        {
            ReplaceDevice(null);
            var device = new Beo4Device(new SerialTransport(portName));
            await device.Connect();
            ReplaceDevice(device);
            PersistDevice();
            SetStatus(StatusType.Ok, $"Connected: {device.Info.Name ?? device.Info.Id}");
        }
        catch (Exception ex) { SetStatus(StatusType.Error, $"Serial failed: {ex.Message}"); }
    }

    public async Task ConnectBluetoothAsync(string? deviceId = null)
    {
        SetStatus(StatusType.Working, deviceId is not null ? $"Connecting to {deviceId}…" : "Scanning Bluetooth…");
        try
        {
            ReplaceDevice(null);
            var device = new Beo4Device(new BluetoothTransport(deviceId));
            await device.Connect();
            ReplaceDevice(device);
            PersistDevice();
            SetStatus(StatusType.Ok, $"Connected: {device.Info.Name ?? device.Info.Id}");
        }
        catch (Exception ex) { SetStatus(StatusType.Error, $"Bluetooth failed: {ex.Message}"); }
    }

    public async Task ConnectPc2Async()
    {
        SetStatus(StatusType.Working, "Connecting to PC2…");
        try
        {
            ReplaceDevice(null);
            var device = new Pc2Device();
            await device.Connect();
            ReplaceDevice(device);
            Settings.LastDevice = DeviceType.PC2;
            Settings.Save();
            SetStatus(StatusType.Ok, "Connected: PC2");
            StateChanged?.Invoke();
        }
        catch (Exception ex) { SetStatus(StatusType.Error, $"PC2 failed: {ex.Message}"); }
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

    public void Disconnect(bool silent = false)
    {
        ReplaceDevice(null);
        if (!silent) SetStatus(StatusType.Idle, "Disconnected");
    }

    public void Dispose() => Disconnect(silent: true);

    // ── Private helpers ───────────────────────────────────────────────────────

    private void ReplaceDevice(IDevice? next)
    {
        _device?.Disconnect();
        _device?.Dispose();
        _device = next;
    }

    private void PersistDevice()
    {
        if (_device is null) return;
        Settings.LastDevice = _device.Info.Type;
        if (_device.Info.Type == DeviceType.Serial) Settings.LastSerial = _device.Info;
        else if (_device.Info.Type == DeviceType.BT) Settings.LastBluetooth = _device.Info;
        Settings.Save();
        StateChanged?.Invoke();
    }

    private void SetStatus(StatusType type, string text)
    {
        StatusType = type;
        StatusText = text;
        StateChanged?.Invoke();
    }
}
