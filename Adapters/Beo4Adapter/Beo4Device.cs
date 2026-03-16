using BeoControl.Interfaces;

namespace Beo4Adapter;

/// <summary>
/// Wraps an <see cref="ITransport"/> (serial or BLE) as an <see cref="IDevice"/>,
/// forwarding text commands to the ESP32 Beo4 firmware.
/// </summary>
public sealed class Beo4Device : IDevice
{
    private readonly ITransport _transport;

    public bool IsConnected => _transport.IsConnected;
    public DeviceInfo Info { get; private set; } = new(DeviceType.USB, null, null);

    public event Action<StatusMessage>? OnStatusChanged;
    public event Action<LogMessage>? OnLog;

    public Beo4Device(ITransport transport)
    {
        _transport = transport;
        //DeviceName = deviceName ?? "ESP32 Beo4";

        _transport.OnStatusChanged += s => OnStatusChanged?.Invoke(s);
        _transport.OnLog += m => OnLog?.Invoke(m);
    }

    public async Task Connect()
    {
        var r = await _transport.Connect();
        Info = r ?? throw new Exception("Transport connect failed");
    }

    public void CancelAutoDetect() => _transport.CancelAutoDetect();
    public void Disconnect() => _transport.Disconnect();

    public void SendCommand(string cmd, string? arg = null)
    {
        var line = arg is not null ? $"{cmd} {arg}" : cmd;
        _transport.SendLine(line);
    }

    public void Dispose() => _transport.Dispose();
}
