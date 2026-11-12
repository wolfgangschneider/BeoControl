using BeoControl.Interfaces;

namespace Beo4Adapter.Transport;

/// <summary>
/// WiFi connection over the firmware's HTTP endpoint (GET /cmd?c=&lt;command&gt;, port 80): one request per
/// command, the reply lines come back in the response; any number of apps at once.
/// Discovery and ".local" resolution are shared with <see cref="TcpTransport"/> (kept as fallback).
/// </summary>
public class RestTransport : ITransport
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly string? _host;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private HttpClient? _http;

    public bool IsConnected => _http is not null;

    public event Action<StatusMessage>? OnStatusChanged;
    public event Action<LogMessage>? OnLog;

    /// <param name="host">mDNS host name from a scan (e.g. "beo4remote-atoms3.local"). Null = scan only.</param>
    public RestTransport(string? host = null)
    {
        _host = host;
    }

    public async Task<DeviceInfo?> Connect()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_host))
                throw new Exception("No WiFi device selected.");
            OnStatusChanged?.Invoke(new DeviceStatusMessage(DeviceStatus.Connecting, $"○ Connecting to {_host}..."));

            // Android (and Linux without avahi) can't resolve ".local": ask the device via mDNS, else the system resolver
            using var timeout = new CancellationTokenSource(RequestTimeout);
            var address = _host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                ? await TcpTransport.ResolveMdnsAsync(_host, timeout.Token) : null;
            var http = new HttpClient { BaseAddress = new Uri($"http://{address?.ToString() ?? _host}/"), Timeout = RequestTimeout };

            string? name = null;
            foreach (var line in await RequestAsync(http, "name"))
                if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                    name = line[5..].Trim();

            _http = http;
            OnStatusChanged?.Invoke(new DeviceStatusMessage(DeviceStatus.Connected, "Connected"));
            return new DeviceInfo(DeviceType.WIFI, name ?? _host, _host);
        }
        catch (Exception ex)
        {
            var msg = ex is OperationCanceledException ? $"Timeout connecting to {_host}" : ex.Message;
            OnStatusChanged?.Invoke(new DeviceStatusMessage(DeviceStatus.Error, $"✗ {msg}"));
            OnLog?.Invoke(new LogMessage(LogLevel.Error, msg));
            return null;
        }
    }

    public Task<List<DeviceInfo>> ScanAsync(CancellationToken ct, Action<StatusMessage>? status = null)
        => TcpTransport.ScanDevices(ct, status);

    public void CancelAutoDetect() { }

    public void Disconnect()
    {
        _http?.Dispose();
        _http = null;
        OnStatusChanged?.Invoke(new DeviceStatusMessage(DeviceStatus.Disconnected, "not connected"));
    }

    public void SendLine(string line)
    {
        if (_http is not { } http)
        {
            OnLog?.Invoke(new LogMessage(LogLevel.Error, "Not connected via WiFi."));
            return;
        }
        Task.Run(async () =>
        {
            // one request at a time, in order: like the line protocol, "status" follows every command
            await _sendLock.WaitAsync();
            try
            {
                foreach (var reply in await RequestAsync(http, line))
                    Emit(reply);
                foreach (var reply in await RequestAsync(http, "status"))
                    Emit(reply);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke(new LogMessage(LogLevel.Error, $"WiFi send error: {ex.Message}"));
            }
            finally
            {
                _sendLock.Release();
            }
        });
    }

    public void Dispose() => Disconnect();

    private static async Task<string[]> RequestAsync(HttpClient http, string command) =>
        (await http.GetStringAsync("cmd?c=" + Uri.EscapeDataString(command)))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // reply lines like the TCP read loop: source status or log line
    private void Emit(string line)
    {
        if (ProtocolStatusParser.TryParseSourceStatus(line, out var source))
            OnStatusChanged?.Invoke(new SourceStatusMessage(source));
        else
            OnLog?.Invoke(new LogMessage(LogLevel.Debug, line));
    }
}
