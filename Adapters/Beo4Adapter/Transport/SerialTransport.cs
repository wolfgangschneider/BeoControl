using BeoControl.Interfaces;

using System.IO.Ports;
using System.Management;

namespace Beo4Adapter.Transport;

public class SerialTransport : ITransport
{
    private SerialPort? _port;
    private readonly string? _forcedPortName;
    //private string _portDescription = "not connected";
    private CancellationTokenSource? _readCts;
    private CancellationTokenSource? _connectCts;

    public bool IsConnected => _port?.IsOpen == true;




    /// <summary>Raised when connection state changes.</summary>
    public event Action<StatusMessage>? OnStatusChanged;

    /// <summary>Raised for log messages (errors, debug info, etc.).</summary>
    public event Action<LogMessage>? OnLog;

    /// <param name="portName">If null, auto-detect. Otherwise use e.g. "COM5".</param>
    public SerialTransport(string? portName = null)
    {
        _forcedPortName = portName;
    }

    public async Task<DeviceInfo?> Connect()
    {
        _connectCts = new CancellationTokenSource();

        DeviceInfo? result = null;
        var ct = _connectCts.Token;
        try
        {
            string portName;
            string? firmwareName = null;

            if (_forcedPortName is null)
            {
                OnStatusChanged?.Invoke(new StatusMessage(StatusType.Working, "○ Scanning serial ports...", StatusKind.Discovery));
                (portName, firmwareName) = AutoDetect(ct,
                    msg => OnStatusChanged?.Invoke(msg));
            }
            else
            {
                portName = _forcedPortName;
                OnStatusChanged?.Invoke(new StatusMessage(StatusType.Working, $"○ Connecting to {portName}...", StatusKind.Connection));
            }

            ct.ThrowIfCancellationRequested();

            _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 500,
                WriteTimeout = 2000,
                NewLine = "\n",
                DtrEnable = false,   // prevent DTR from triggering ESP32 auto-reset on open
                RtsEnable = false,
            };
            _port.Open();
            if (!OperatingSystem.IsWindows())
                await Task.Delay(1500, ct);    // on Linux, DTR resets the ESP32 on open — wait for boot
            _port.DiscardInBuffer();           // discard boot banner

            var portLabel = _forcedPortName is null ? $"AutoDetect ({portName})" : portName;

            if (firmwareName is null)
            {
                // Ask firmware for its name; read up to 5 lines looking for "Name: ..."
                _port.WriteLine("name");
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        var reply = _port.ReadLine().TrimEnd('\r', '\n');
                        if (reply.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                        {
                            result = new DeviceInfo(DeviceType.USB, reply.Substring(5).Trim(), portName);
                            break;
                        }
                    }
                    catch (TimeoutException) { break; }
                }
            }

            // Port opened successfully — use port name as fallback label if firmware didn't respond
            result ??= new DeviceInfo(DeviceType.USB, portLabel, portName);

            _port.DiscardInBuffer();           // discard any leftover bytes
            _port.ReadTimeout = 200;
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, "Connected", StatusKind.Connection));

            StartReadLoop();

            return result;
        }
        catch (OperationCanceledException)
        {

            return null;
        }
        catch (AmbiguousPortException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            OnStatusChanged?.Invoke(new StatusMessage(StatusType.Error, $"✗ {msg}", StatusKind.Connection));
            OnLog?.Invoke(new LogMessage(LogLevel.Error, msg));
            return null;
        }

    }

    public void CancelAutoDetect() => _connectCts?.Cancel();

    public void Disconnect()
    {
        _connectCts?.Cancel();
        _readCts?.Cancel();
        _readCts = null;
        _port?.Close();
        _port?.Dispose();
        _port = null;
        OnStatusChanged?.Invoke(new StatusMessage(StatusType.Idle, "not connected", StatusKind.Connection));
    }

    public void SendLine(string line)
    {
        if (_port is null || !_port.IsOpen)
        {
            OnLog?.Invoke(new LogMessage(LogLevel.Error, "Not connected. Use /port COMx to connect."));
            return;
        }
        try { _port.WriteLine(line); _port.WriteLine("status"); }
        catch (Exception ex) { OnLog?.Invoke(new LogMessage(LogLevel.Error, $"Send error: {ex.Message}")); }
    }

    public string? TryReadLine()
    {
        if (_port is null || !_port.IsOpen) return null;
        try
        {
            var line = _port.ReadLine().TrimEnd('\r', '\n');
            if (line.Length > 0)
            {
                if (ProtocolStatusParser.TryParseSourceStatus(line, out var sourceStatus))
                    OnStatusChanged?.Invoke(new StatusMessage(StatusType.Ok, sourceStatus, StatusKind.Source));
                else
                    OnLog?.Invoke(new LogMessage(LogLevel.Debug, line));
            }
            return line;
        }
        catch (TimeoutException) { return null; }
    }

    private void StartReadLoop()
    {
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && IsConnected)
            {
                TryReadLine();
                await Task.Delay(80, token).ContinueWith(_ => { });
            }
        });
    }

    public void Dispose() => Disconnect();

    /// <summary>
    /// Returns all available COM ports with their WMI friendly names and PNP device IDs.
    /// Key = port name (e.g. "COM5"), Value = (caption, pnpDeviceId).
    /// </summary>
    public static Dictionary<string, (string Caption, string PnpId)> ListPortsDetailed()
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        try
        {


            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption, PNPDeviceID FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'");
            foreach (ManagementObject obj in searcher.Get())
            {



                var caption = obj["Caption"]?.ToString() ?? "";
                var pnpId = obj["PNPDeviceID"]?.ToString() ?? "";
                var start = caption.LastIndexOf("(COM", StringComparison.Ordinal);
                if (start < 0) continue;
                var end = caption.IndexOf(')', start);
                if (end < 0) continue;
                var port = caption.Substring(start + 1, end - start - 1);
                result[port] = (caption, pnpId);
            }
        }
        catch { /* fallback: no WMI */ }
        return result;
    }

    /// <summary>
    /// Returns all available COM ports with their WMI friendly names.
    /// Key = port name (e.g. "COM5"), Value = friendly description.
    /// </summary>
    public static Dictionary<string, string> ListPorts()
    {
        var detailed = ListPortsDetailed();
        if (detailed.Count > 0)
            return detailed.ToDictionary(kv => kv.Key, kv => kv.Value.Caption, StringComparer.OrdinalIgnoreCase);
        // WMI fallback
        return SerialPort.GetPortNames().ToDictionary(p => p, p => p, StringComparer.OrdinalIgnoreCase);
    }

    public Task<List<DeviceInfo>> ScanAsync(CancellationToken ct, Action<StatusMessage>? status = null)
        => Task.Run(() => ScanDevices(ct, status), ct);

    /// <summary>
    /// Probes all candidate COM ports and returns every port that responds to the firmware "name" command.
    /// Unlike <see cref="AutoDetect"/>, this returns ALL responders instead of stopping at the first.
    /// </summary>
    public static List<DeviceInfo> ScanDevices(
        CancellationToken ct, Action<StatusMessage>? status = null)
    {
        var ports = ListPortsDetailed();
        List<string> candidates;
        if (ports.Count > 0)
        {
            var atom   = ports.Where(kv => kv.Value.PnpId.Contains("VID_303A&PID_1001", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key);
            var cp210x = ports.Where(kv => kv.Value.Caption.Contains("CP210",   StringComparison.OrdinalIgnoreCase) ||
                                           kv.Value.Caption.Contains("Silicon", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key);
            candidates = atom.Union(cp210x).Union(ports.Keys).Distinct().ToList();
        }
        else
        {
            // WMI not available (Linux) — fall back to OS port list
            candidates = SerialPort.GetPortNames().ToList();
        }

        var found = new List<DeviceInfo>();
        foreach (var port in candidates)
        {
            if (ct.IsCancellationRequested) break;
            status?.Invoke(new StatusMessage(StatusType.Working, $"○ Probing {port}...", StatusKind.Discovery));
            var result = ProbePort(port);
            if (result is not null)
                found.Add(new DeviceInfo(DeviceType.USB, result.Value.Name ?? port, result.Value.Port));
        }
        return found;
    }

    /// <summary>
    /// Detects the correct COM port by probing each candidate: opens the port,
    /// sends "name", and confirms the board responds with a "Name: ..." line.
    /// Returns the port name and firmware name (if the board responded).
    /// </summary>
    private static (string PortName, string? FirmwareName) AutoDetect(CancellationToken ct, Action<StatusMessage>? status = null)
    {
        var ports = ListPortsDetailed();

        List<string> candidates;
        if (ports.Count > 0)
        {
            var atom   = ports.Where(kv => kv.Value.PnpId.Contains("VID_303A&PID_1001", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key);
            var cp210x = ports.Where(kv => kv.Value.Caption.Contains("CP210",   StringComparison.OrdinalIgnoreCase) ||
                                           kv.Value.Caption.Contains("Silicon", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key);
            candidates = atom.Union(cp210x).Union(ports.Keys).ToList();
        }
        else
        {
            // WMI not available (Linux) — fall back to OS port list
            candidates = SerialPort.GetPortNames().ToList();
        }

        if (candidates.Count == 0)
            throw new Exception("No serial ports found. Is the ESP32 plugged in?");

        (string Port, string? Name)? confirmed = null;
        foreach (var port in candidates)
        {
            ct.ThrowIfCancellationRequested();
            status?.Invoke(new StatusMessage(StatusType.Working, $"○ Probing {port}...", StatusKind.Discovery));
            confirmed = ProbePort(port);
            if (confirmed is not null)
                break;
        }

        if (confirmed is not null)
            return confirmed.Value;

        if (candidates.Count == 1)
            return (candidates[0], null);

        throw new AmbiguousPortException(
            ports.ToDictionary(kv => kv.Key, kv => kv.Value.Caption, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Opens <paramref name="portName"/>, sends "name", and waits for a "Name: ..." response.
    /// Returns the port + firmware name on success, or null if the port cannot be opened or does not respond.
    /// </summary>
    private static (string Port, string? Name)? ProbePort(string portName)
    {
        try
        {
            using var probe = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 500,
                WriteTimeout = 2000,
                NewLine = "\n",
                DtrEnable = false,   // prevent DTR from triggering ESP32 auto-reset on open
                RtsEnable = false,
            };
            probe.Open();
            if (!OperatingSystem.IsWindows())
                Thread.Sleep(1500);            // on Linux, DTR resets the ESP32 on open — wait for boot
            probe.DiscardInBuffer();
            probe.WriteLine("name");
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    var reply = probe.ReadLine().TrimEnd('\r', '\n');
                    if (reply.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                        return (portName, reply.Substring(5).Trim());
                }
                catch (TimeoutException) { break; }
            }
            // Port opened but board did not send a "Name:" line — not our device
            return null;
        }
        catch
        {
            return null;
        }
    }
}

