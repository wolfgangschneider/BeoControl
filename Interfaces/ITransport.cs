namespace BeoControl.Interfaces;

public enum CommandId
{
    Tv,
    Radio,
    Cd,
    Cd2,
    Phono,
    Dvd,
    Sat,
    Vtape,
    Pc,
    Doorcam,
    Light,
    AAux,
    VAux,
    Atape,
    Atape2,
    Link,
    Text,
    SpeakerDemo,
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,
    VolumeUp,
    VolumeDown,
    Mute,
    Loudness,
    Standby,
    Off,
    AllStandby,
    AllOff,
    Go,
    Goto,
    Play,
    Stop,
    Record,
    Up,
    Down,
    Left,
    Right,
    Menu,
    Exit,
    Return,
    Select,
    List,
    Index,
    Bass,
    Treble,
    Balance,
    Speaker,
    Red,
    Green,
    Blue,
    Yellow,
    Store,
    Clear,
    Tune,
    Clock,
    Format,
    Picture,
    Turn,
    Av,
    Pc2Dvd2,
    Pc2On,
    Pc2BassUp,
    Pc2BassDown,
    Pc2TrebleUp,
    Pc2TrebleDown,
    Pc2BalanceUp,
    Pc2BalanceDown,
    AppHelp,
    AppClear,
    AppDebug,
    AppExit,
    AppPort,
    AppPortScan,
    AppBt,
    AppBtScan,
    AppBtLast,
    AppPc2,
}

public enum CommandCategory
{
    Source,
    Number,
    Volume,
    Power,
    Transport,
    Navigation,
    Sound,
    Color,
    Misc,
    Pc2Source,
    Pc2Tone,
    AppCommands,
}

/// <summary>Describes a single command that a device can execute (used for help + tab completion).</summary>
public class BeoCommand
{
    public BeoCommand(CommandId id, string cmd, string remoteLabel, CommandCategory category)
    {
        Id = id;
        Cmd = cmd;
        RemoteLabel = remoteLabel;
        Category = category;
    }

    public CommandId Id { get; }
    public string Cmd { get; init; }
    public string RemoteLabel { get; init; }
    public CommandCategory Category { get; init; }
    public string? ParamHint { get; set; }
    public string DisplayLabel => !string.IsNullOrWhiteSpace(AddIn)
        ? AddIn
        : string.IsNullOrWhiteSpace(RemoteLabel) ? Cmd : RemoteLabel;

    public List<BeoCommand>? SubCommands { get; set; }
    public string? AddIn { get; set; }
}

public enum StatusType { Idle, Working, Ok, Source, Error }
public enum StatusKind { Connection, Discovery, Source, AudioSetup, Transport, Info }

/// <summary>One-line status bar message with semantic type for color rendering.</summary>
public record StatusMessage(StatusType Type, string Text, StatusKind Kind = StatusKind.Info);

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>Appended log entry with severity level.</summary>
public record LogMessage(LogLevel Level, string Text);

public enum DeviceType { USB, BT, PC2 }

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
