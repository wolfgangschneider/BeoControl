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

    public async Task AutoConnectAsync()
    {
        switch (Settings.LastDevice)
        {
            case DeviceType.USB when Settings.LastSerial?.Id is { } port:
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

    public async Task ConnectBluetoothAsync(string? deviceId = null)
    {
        Notify(StatusType.Working, deviceId is not null ? $"Connecting to {deviceId}…" : "Scanning Bluetooth…");
        try
        {
            ReplaceDevice(null);
            var device = new Beo4Device(new BluetoothTransport(deviceId));
            await device.Connect();
            ReplaceDevice(device);
            PersistDevice();
            Notify(StatusType.Ok, $"Connected: {device.Info.Name ?? device.Info.Id}");
        }
        catch (Exception ex) { Notify(StatusType.Error, $"Bluetooth failed: {ex.Message}"); }
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

    public void Disconnect(bool silent = false)
    {
        ReplaceDevice(null);
        if (!silent) Notify(StatusType.Idle, "Disconnected");
    }

    public void Dispose() => Disconnect(silent: true);

    // ── Private helpers ───────────────────────────────────────────────────────

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
                SyncPc2AudioSetup(parsedSetup, save: true);
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
