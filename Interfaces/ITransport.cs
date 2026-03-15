namespace BeoControl.Interfaces;

/// <summary>Describes a single command that a device can execute (used for help + tab completion).</summary>
public record CommandInfo(string Name, string Description, string Category, string? ParamHint = null);

public enum StatusType { Idle, Working, Ok, Error }

/// <summary>One-line status bar message with semantic type for color rendering.</summary>
public record StatusMessage(StatusType Type, string Text);

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>Appended log entry with severity level.</summary>
public record LogMessage(LogLevel Level, string Text);

public enum DeviceType { Serial, BT, PC2 }

/// <summary>Identity info for a connected device (type, friendly name, reconnect id).</summary>
public record DeviceInfo(DeviceType Type, string? Name, string? Id);

/// <summary>Unified abstraction for any B&O controller device (ESP32 serial/BLE, PC2 USB, …).</summary>
public interface IDevice : IDisposable
{
    bool IsConnected { get; }
    DeviceInfo Info { get; }

    event Action<StatusMessage>? OnStatusChanged;
    event Action<LogMessage>? OnLog;

    Task Connect();
    /// <summary>Cancel an in-progress auto-detect / BLE scan without disconnecting an established connection.</summary>
    void CancelAutoDetect();
    void Disconnect();

    /// <summary>Send a named command with an optional argument (number or token).</summary>
    void SendCommand(string cmd, string? arg = null);
}

public interface ITransport : IDisposable
{
    bool IsConnected { get; }

    event Action<StatusMessage>? OnStatusChanged;
    event Action<LogMessage>? OnLog;

    /// <summary>Connect to the transport. Returns DeviceInfo on success, null on failure.</summary>
    Task<DeviceInfo?> Connect();

    /// <summary>Scan for available devices of this transport type.</summary>
    Task<List<DeviceInfo>> ScanAsync(CancellationToken ct, Action<StatusMessage>? status = null);

    /// <summary>Cancel an in-progress auto-detect / BLE scan without disconnecting an established connection.</summary>
    void CancelAutoDetect();

    void Disconnect();

    /// <summary>Send a command line to the device.</summary>
    void SendLine(string line);
}

/// <summary>Thrown when multiple candidate ports are found and the user must choose.</summary>
public class AmbiguousPortException(Dictionary<string, string> ports) : Exception
{
    public Dictionary<string, string> Ports { get; } = ports;
}
