using BeoControl.Interfaces;
using InTheHand.Bluetooth;
using System.Text;

namespace Beo4Adapter.Transport;

/// <summary>
/// BLE NUS (Nordic UART Service) transport for the M5 Atom S3 Beo4Remote firmware.
/// Uses InTheHand.BluetoothLE for cross-platform BLE support.
/// </summary>
public class BluetoothTransport : ITransport
{
    internal const string NusServiceUuidString = "6E400001-B5A3-F393-E0A9-E50E24DCCA9E";
    internal static readonly BluetoothUuid NusService = BluetoothUuid.FromGuid(Guid.Parse(NusServiceUuidString));
    internal static readonly BluetoothUuid NusRxChar = BluetoothUuid.FromGuid(Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E"));
    internal static readonly BluetoothUuid NusTxChar = BluetoothUuid.FromGuid(Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E"));

    private BluetoothDevice? _device;
    private GattCharacteristic? _rxChar;
    private GattCharacteristic? _txChar;
    private CancellationTokenSource? _cts;
    private readonly string _deviceNamePrefix;
    private readonly string? _forcedDeviceId;
    private readonly string? _preferredDeviceName;
    // Discovery varies by platform, but connection and NUS traffic handling stay shared here.
    private readonly IBluetoothDiscovery _discovery;

    public bool IsConnected { get; private set; }

    public event Action<StatusMessage>? OnStatusChanged;
    public event Action<LogMessage>? OnLog;

    public BluetoothTransport(string? deviceId = null, string? preferredDeviceName = null)
    {
        _deviceNamePrefix = "Beo4Remote";
        _forcedDeviceId = deviceId;
        _preferredDeviceName = preferredDeviceName;
        _discovery = BluetoothDiscoveryFactory.Create();
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
        CleanupConnectionState();
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
            catch (Exception ex)
            {
                OnLog?.Invoke(new LogMessage(LogLevel.Error, $"BLE send error: {ex.Message}"));
            }
        });
    }

    public void Dispose() => Disconnect();

    private async Task<DeviceInfo?> ConnectAsync(CancellationToken ct)
    {
        try
        {
            return await ConnectOnceAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            CleanupConnectionState();
            var msg = $"BLE connect error: [{ex.GetType().Name}] {ex.Message}  HResult=0x{ex.HResult:X8}";
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Error, $"✗ {BuildUserFacingBluetoothError(ex)}", StatusKind.Connection));
            OnLog?.Invoke(new LogMessage(LogLevel.Error, msg));
            return null;
        }
    }

    private async Task<DeviceInfo?> ConnectOnceAsync(CancellationToken ct)
    {
        BluetoothDevice device;

        if (_forcedDeviceId is not null)
        {
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Working, BuildConnectStatusText(_forcedDeviceId), StatusKind.Connection));
            var direct = await BluetoothDevice.FromIdAsync(_forcedDeviceId);
            if (direct is null)
                throw new Exception($"BLE device '{_forcedDeviceId}' not found");

            ct.ThrowIfCancellationRequested();
            device = direct;
            OnLog?.Invoke(new LogMessage(LogLevel.Debug, $"BLE direct: {device.Name} ({device.Id})"));
        }
        else
        {
            (device, _) = await AutoDetect(_deviceNamePrefix, _preferredDeviceName, _discovery, ct, msg => OnStatusChanged?.Invoke(msg));
            ct.ThrowIfCancellationRequested();
        }

        await OpenGattAsync(device, ct);
        return new DeviceInfo(DeviceType.BT, device.Name, device.Id);
    }

    public async Task<List<DeviceInfo>> ScanAsync(CancellationToken ct, Action<StatusMessage>? status = null)
    {
        status?.Invoke(new StatusMessage(StatusType.Working, $"○ Scanning for BLE '{_deviceNamePrefix}'...", StatusKind.Discovery));
        var devices = await _discovery.DiscoverAsync(_deviceNamePrefix, ct, status);
        ct.ThrowIfCancellationRequested();

        return devices
            .Where(device => device is not null)
            .Select(device => new DeviceInfo(DeviceType.BT, device.Name ?? device.Id ?? "Unknown", device.Id))
            .ToList();
    }

    public static async Task<List<DeviceInfo>> ScanDevices(string namePrefix, CancellationToken ct, Action<StatusMessage>? status = null)
    {
        status?.Invoke(new StatusMessage(StatusType.Working, $"○ Scanning for BLE '{namePrefix}'...", StatusKind.Discovery));
        var devices = await BluetoothDiscoveryFactory.Create().DiscoverAsync(namePrefix, ct, status);
        ct.ThrowIfCancellationRequested();

        return devices
            .Where(device => device is not null)
            .Select(device => new DeviceInfo(DeviceType.BT, device.Name ?? device.Id ?? "Unknown", device.Id))
            .ToList();
    }

    private static async Task<(BluetoothDevice Device, string Id)> AutoDetect(
        string namePrefix,
        string? preferredDeviceName,
        IBluetoothDiscovery discovery,
        CancellationToken ct,
        Action<StatusMessage>? status = null)
    {
        status?.Invoke(new StatusMessage(StatusType.Working, $"○ Scanning for BLE '{namePrefix}'...", StatusKind.Discovery));
        var devices = await discovery.DiscoverAsync(namePrefix, ct, status);
        ct.ThrowIfCancellationRequested();

        var device = SelectPreferredDevice(
            devices.Where(d => !string.IsNullOrWhiteSpace(d.Id)),
            preferredDeviceName);

        if (device is null)
            throw new Exception(BuildBluetoothNotFoundMessage(namePrefix));

        return (device, device.Id!);
    }

    private static BluetoothDevice? SelectPreferredDevice(IEnumerable<BluetoothDevice> devices, string? preferredDeviceName)
    {
        if (!string.IsNullOrWhiteSpace(preferredDeviceName))
        {
            var preferred = devices.FirstOrDefault(device =>
                string.Equals(device.Name, preferredDeviceName, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return preferred;
        }

        return devices.FirstOrDefault();
    }

    private static string BuildBluetoothNotFoundMessage(string namePrefix)
    {
        var message = $"BLE scan timeout — '{namePrefix}' not found. Firmware flashed? Device powered?";
        return AppendMacBluetoothPermissionHint(message);
    }

    private static string BuildUserFacingBluetoothError(Exception ex)
    {
        var message = $"{ex.GetType().Name}: {ex.Message}";
        return AppendMacBluetoothPermissionHint(message);
    }

    private static string AppendMacBluetoothPermissionHint(string message)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsMacCatalyst())
            return message;

        return $"{message} Check System Settings > Privacy & Security > Bluetooth and allow the host app (for example Terminal, iTerm, Rider, or Visual Studio).";
    }

    /// <summary>
    /// Connects GATT, discovers NUS service and characteristics, starts notifications.
    /// Throws on any failure so ConnectAsync can handle it uniformly.
    /// </summary>
    private async Task OpenGattAsync(BluetoothDevice device, CancellationToken ct)
    {
        _device = device;
        OnLog?.Invoke(new LogMessage(LogLevel.Debug, $"BLE device: {device.Name} ({device.Id})"));

        await device.Gatt.ConnectAsync();
        ct.ThrowIfCancellationRequested();
        OnLog?.Invoke(new LogMessage(LogLevel.Debug, $"GATT connected: {device.Gatt.IsConnected}"));

        if (!device.Gatt.IsConnected)
        {
            device.Gatt.Disconnect();
            throw new Exception("GATT connection failed");
        }

        var allServices = await device.Gatt.GetPrimaryServicesAsync(null);
        var service = allServices.FirstOrDefault(candidate => candidate.Uuid == NusService);
        if (service is null)
        {
            device.Gatt.Disconnect();
            var found = string.Join(", ", allServices.Select(candidate => candidate.Uuid.ToString()[..8]));
            throw new Exception($"NUS service not found. Services seen: [{found}]");
        }

        var rxCharacteristic = await service.GetCharacteristicAsync(NusRxChar);
        var txCharacteristic = await service.GetCharacteristicAsync(NusTxChar);
        if (rxCharacteristic is null || txCharacteristic is null)
        {
            device.Gatt.Disconnect();
            throw new Exception("NUS RX/TX characteristics not found");
        }

        _rxChar = rxCharacteristic;
        _txChar = txCharacteristic;

        txCharacteristic.CharacteristicValueChanged += OnNotification;
        await txCharacteristic.StartNotificationsAsync();

        device.GattServerDisconnected += (_, _) =>
        {
            IsConnected = false;
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Idle, "BLE disconnected", StatusKind.Connection));
        };

        IsConnected = true;
        OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, $"Beo4Remote BLE ({device.Name})", StatusKind.Connection));
        await WriteAsync("name\n");
    }

    private void CleanupConnectionState()
    {
        if (_txChar is not null)
        {
            _txChar.CharacteristicValueChanged -= OnNotification;
            _txChar = null;
        }

        _rxChar = null;
        _device?.Gatt.Disconnect();
        _device = null;
        IsConnected = false;
    }

    private static string BuildConnectStatusText(string deviceId) =>
        $"○ Connecting to BLE {deviceId}...";

    private async Task WriteAsync(string text)
    {
        if (_rxChar is null)
            return;

        var bytes = Encoding.UTF8.GetBytes(text);
        await _rxChar.WriteValueWithResponseAsync(bytes);
    }

    private void OnNotification(object? sender, GattCharacteristicValueChangedEventArgs args)
    {
        if (args.Value is null || args.Value.Length == 0)
            return;

        var text = Encoding.UTF8.GetString(args.Value).TrimEnd('\r', '\n');
        if (string.IsNullOrEmpty(text))
            return;

        if (text.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, "Connected", StatusKind.Connection));
        else if (ProtocolStatusParser.TryParseSourceStatus(text, out var sourceStatus))
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Source, sourceStatus, StatusKind.Source));
        else
            OnLog?.Invoke(new LogMessage(LogLevel.Debug, text));
    }
}
