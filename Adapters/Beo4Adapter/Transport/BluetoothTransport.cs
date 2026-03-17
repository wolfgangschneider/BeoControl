using BeoControl.Interfaces;

using InTheHand.Bluetooth;

using System.Text;

namespace Beo4Adapter.Transport;

/// <summary>
/// BLE NUS (Nordic UART Service) transport for the M5 Atom S3 Beo4Remote firmware.
/// Uses InTheHand.BluetoothLE for cross-platform BLE support (Windows/Linux/macOS).
/// </summary>
public class BluetoothTransport : ITransport
{
    // Nordic UART Service UUIDs — must match BtChannel.h
    private static readonly BluetoothUuid NusService = BluetoothUuid.FromGuid(Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E"));
    private static readonly BluetoothUuid NusRxChar = BluetoothUuid.FromGuid(Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E")); // write
    private static readonly BluetoothUuid NusTxChar = BluetoothUuid.FromGuid(Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E")); // notify

    private BluetoothDevice? _device;
    private GattCharacteristic? _rxChar;
    private GattCharacteristic? _txChar;
    private CancellationTokenSource? _cts;
    private readonly string _deviceNamePrefix;
    private readonly string? _forcedDeviceId;

    public bool IsConnected { get; private set; }



    public event Action<StatusMessage>? OnStatusChanged;
    public event Action<LogMessage>? OnLog;



    /// <param name="deviceNamePrefix">BLE device name prefix to scan for. Defaults to "Beo4Remote".</param>
    /// <param name="deviceId">Hardware device ID (e.g. "48CA43B76EC9"). When set, skips scan and connects directly.</param>
    public BluetoothTransport(string? deviceId = null)
    {
        _deviceNamePrefix = "Beo4Remote";
        _forcedDeviceId = deviceId;
    }

    public async Task<DeviceInfo?> Connect()
    {
        _cts = new CancellationTokenSource();
        return await ConnectAsync(_cts.Token);
    }

    public void CancelAutoDetect() => _cts?.Cancel();

    public void Disconnect()
    {
        _cts?.Cancel();
        if (_txChar != null)
        {
            _txChar.CharacteristicValueChanged -= OnNotification;
            _txChar = null;
        }
        _rxChar = null;
        _device?.Gatt.Disconnect();
        _device = null;
        IsConnected = false;
        OnStatusChanged?.Invoke(new StatusMessage(StatusType.Idle, "BLE disconnected", StatusKind.Connection));
    }

    public void SendLine(string line)
    {
        if (!IsConnected || _rxChar is null)
        {
            OnLog?.Invoke(new LogMessage(LogLevel.Error, "Not connected via Bluetooth."));
            return;
        }
        Task.Run(async () =>
        {
            try
            {
                await WriteAsync(line + "\n");
                await WriteAsync("status\n");
            }
            catch (Exception ex) { OnLog?.Invoke(new LogMessage(LogLevel.Error, $"BLE send error: {ex.Message}")); }
        });
    }

    public void Dispose() => Disconnect();

    // ── private ──────────────────────────────────────────────────────────

    private async Task<DeviceInfo?> ConnectAsync(CancellationToken ct)
    {
        try
        {
            BluetoothDevice device;
            string? discoveredId = null;

            if (_forcedDeviceId is not null)
            {
                // Direct connect — skip scan entirely (mirrors /port COMx in SerialTransport)
                OnStatusChanged?.Invoke(new StatusMessage(StatusType.Working, $"○ Connecting to BLE {_forcedDeviceId}...", StatusKind.Connection));
                var d = await BluetoothDevice.FromIdAsync(_forcedDeviceId);
                if (d is null) throw new Exception($"BLE device '{_forcedDeviceId}' not found");
                ct.ThrowIfCancellationRequested();
                device = d;
                OnLog?.Invoke(new LogMessage(LogLevel.Debug, $"BLE direct: {device.Name} ({device.Id})"));
            }
            else
            {
                (device, discoveredId) = await AutoDetect(_deviceNamePrefix, ct,
                    msg => OnStatusChanged?.Invoke(msg));
                ct.ThrowIfCancellationRequested();
            }

            await OpenGattAsync(device, ct);



            return new DeviceInfo(DeviceType.BT, device.Name, device.Id);

        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            var msg = $"BLE connect error: [{ex.GetType().Name}] {ex.Message}  HResult=0x{ex.HResult:X8}";
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Error, $"✗ {ex.GetType().Name}: {ex.Message}", StatusKind.Connection));
            OnLog?.Invoke(new LogMessage(LogLevel.Error, msg));
            return null;
        }

    }

    public async Task<List<DeviceInfo>> ScanAsync(CancellationToken ct, Action<StatusMessage>? status = null)
    {
        status?.Invoke(new StatusMessage(StatusType.Working, $"○ Scanning for BLE '{_deviceNamePrefix}'...", StatusKind.Discovery));
        //unfortunately is null without device
        ////OnStatusChanged?.Invoke(new StatusMessage(StatusType.Working, $"○BBB Scanning for BLE '{_deviceNamePrefix}'..."));
        var filter = new BluetoothLEScanFilter { NamePrefix = _deviceNamePrefix };
        var options = new RequestDeviceOptions();
        options.Filters.Add(filter);
        options.AcceptAllDevices = false;

        var devices = await Bluetooth.ScanForDevicesAsync(options, ct);
        ct.ThrowIfCancellationRequested();

        return devices
            .Where(d => d.Name?.StartsWith(_deviceNamePrefix, StringComparison.OrdinalIgnoreCase) == true)
            .Select(d => new DeviceInfo(DeviceType.BT, d.Name ?? d.Id, d.Id))
            .ToList();

    }

    /// <summary>
    /// Scans for ALL BLE devices whose name starts with <paramref name="namePrefix"/>.
    /// Returns every match as (Id, Name). Returns an empty list if none found.
    /// </summary>
    public static async Task<List<DeviceInfo>> ScanDevices(
        string namePrefix, CancellationToken ct, Action<StatusMessage>? status = null)
    {
        status?.Invoke(new StatusMessage(StatusType.Working, $"○ Scanning for BLE '{namePrefix}'...", StatusKind.Discovery));

        var filter = new BluetoothLEScanFilter { NamePrefix = namePrefix };
        var options = new RequestDeviceOptions();
        options.Filters.Add(filter);
        options.AcceptAllDevices = false;

        var devices = await Bluetooth.ScanForDevicesAsync(options, ct);
        ct.ThrowIfCancellationRequested();

        return devices
            .Where(d => d.Name?.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase) == true)
            .Select(d => new DeviceInfo(DeviceType.BT, d.Name ?? d.Id, d.Id))
            .ToList();
    }

    /// <summary>
    /// Scans for the first BLE device whose name starts with <paramref name="namePrefix"/>.
    /// Returns the device and its hardware ID.  Throws if no device is found.
    /// </summary>
    private static async Task<(BluetoothDevice Device, string Id)> AutoDetect(
        string namePrefix, CancellationToken ct, Action<StatusMessage>? status = null)
    {
        status?.Invoke(new StatusMessage(StatusType.Working, $"○ Scanning for BLE '{namePrefix}'...", StatusKind.Discovery));

        var filter = new BluetoothLEScanFilter { NamePrefix = namePrefix };
        var options = new RequestDeviceOptions();
        options.Filters.Add(filter);
        options.AcceptAllDevices = false;

        var devices = await Bluetooth.ScanForDevicesAsync(options, ct);
        ct.ThrowIfCancellationRequested();

        var device = devices.FirstOrDefault(d =>
            d.Name?.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase) == true);

        if (device is null)
            throw new Exception($"BLE scan timeout — '{namePrefix}' not found. Firmware flashed? Device powered?");

        return (device, device.Id);
    }

    /// <summary>
    /// Connects GATT, discovers NUS service and characteristics, starts notifications.
    /// Throws on any failure so <see cref="ConnectAsync"/> can handle it uniformly.
    /// </summary>
    private async Task OpenGattAsync(BluetoothDevice device, CancellationToken ct)
    {
        _device = device;   // set early so Disconnect() can always call Gatt.Disconnect()
        OnLog?.Invoke(new LogMessage(LogLevel.Debug, $"BLE device: {device.Name} ({device.Id})"));

        await device.Gatt.ConnectAsync();
        ct.ThrowIfCancellationRequested();
        OnLog?.Invoke(new LogMessage(LogLevel.Debug, $"GATT connected: {device.Gatt.IsConnected}"));

        if (!device.Gatt.IsConnected)
        {
            device.Gatt.Disconnect();
            throw new Exception("GATT connection failed");
        }

        var allSvcs = await device.Gatt.GetPrimaryServicesAsync(null);
        var svc = allSvcs.FirstOrDefault(s => s.Uuid == NusService);
        if (svc is null)
        {
            device.Gatt.Disconnect();
            var found = string.Join(", ", allSvcs.Select(s => s.Uuid.ToString()[..8]));
            throw new Exception($"NUS service not found. Services seen: [{found}]");
        }

        var rxChar = await svc.GetCharacteristicAsync(NusRxChar);
        var txChar = await svc.GetCharacteristicAsync(NusTxChar);
        if (rxChar is null || txChar is null)
        {
            device.Gatt.Disconnect();
            throw new Exception("NUS RX/TX characteristics not found");
        }

        _rxChar = rxChar;
        _txChar = txChar;

        txChar.CharacteristicValueChanged += OnNotification;
        await txChar.StartNotificationsAsync();

        device.GattServerDisconnected += (_, _) =>
        {
            IsConnected = false;
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Idle, "BLE disconnected", StatusKind.Connection));
        };

        IsConnected = true;
        OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, $"Beo4Remote BLE ({device.Name})", StatusKind.Connection));
        await WriteAsync("name\n");
    }

    private async Task WriteAsync(string text)
    {
        if (_rxChar is null) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        await _rxChar.WriteValueWithResponseAsync(bytes);
    }

    private void OnNotification(object? sender, GattCharacteristicValueChangedEventArgs args)
    {
        var text = Encoding.UTF8.GetString(args.Value).TrimEnd('\r', '\n');
        if (string.IsNullOrEmpty(text)) return;

        if (text.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, $"Connected", StatusKind.Connection));
        else if (ProtocolStatusParser.TryParseSourceStatus(text, out var sourceStatus))
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, sourceStatus, StatusKind.Source));
        else
            OnLog?.Invoke(new LogMessage(LogLevel.Debug, text));
    }
}
