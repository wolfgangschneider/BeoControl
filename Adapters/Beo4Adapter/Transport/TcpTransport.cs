using BeoControl.Interfaces;

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Beo4Adapter.Transport;

/// <summary>
/// Line-based TCP connection to the ESP32 firmware's WifiChannel (port 23).
/// Devices are discovered via mDNS (service "_beo4._tcp.local").
/// </summary>
public class TcpTransport : ITransport
{
    public const int Port = 23;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NameTimeout    = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ScanDuration   = TimeSpan.FromSeconds(2);
    private const string ServiceName = "_beo4._tcp.local";

    private readonly string? _host;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    public bool IsConnected => _client?.Connected == true;

    public event Action<StatusMessage>? OnStatusChanged;
    public event Action<LogMessage>? OnLog;

    /// <param name="host">mDNS host name from a scan (e.g. "beo4remote-atom.local"). Null = scan only.</param>
    public TcpTransport(string? host = null)
    {
        _host = host;
    }

    public async Task<DeviceInfo?> Connect()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            if (string.IsNullOrWhiteSpace(_host))
                throw new Exception("No WiFi device selected.");
            OnStatusChanged?.Invoke(new DeviceStatusMessage(DeviceStatus.Connecting, $"○ Connecting to {_host}..."));

            _client = new TcpClient { NoDelay = true };
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(ConnectTimeout);
                // Android (and Linux without avahi) can't resolve ".local": ask the device via mDNS, else the system resolver
                if (_host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                    && await ResolveMdnsAsync(_host, timeout.Token) is { } address)
                    await _client.ConnectAsync(address, Port, timeout.Token);
                else
                    await _client.ConnectAsync(_host, Port, timeout.Token);
            }

            var stream = _client.GetStream();
            _reader = new StreamReader(stream, Encoding.ASCII);
            _writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n", AutoFlush = true };

            // Ask firmware for its name; wait briefly for "Name: ..."
            await _writer.WriteLineAsync("name");
            string? name = null;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(NameTimeout);
                try
                {
                    while (name is null && await _reader.ReadLineAsync(timeout.Token) is { } line)
                        if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                            name = line[5..].Trim();
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* no name reply */ }
            }

            OnStatusChanged?.Invoke(new DeviceStatusMessage(DeviceStatus.Connected, "Connected"));
            StartReadLoop(ct);
            return new DeviceInfo(DeviceType.WIFI, name ?? _host, _host);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Cleanup();
            return null;
        }
        catch (Exception ex)
        {
            var msg = ex is OperationCanceledException ? $"Timeout connecting to {_host}" : ex.Message;
            Cleanup();
            OnStatusChanged?.Invoke(new DeviceStatusMessage(DeviceStatus.Error, $"✗ {msg}"));
            OnLog?.Invoke(new LogMessage(LogLevel.Error, msg));
            return null;
        }
    }

    public Task<List<DeviceInfo>> ScanAsync(CancellationToken ct, Action<StatusMessage>? status = null)
        => ScanDevices(ct, status);

    /// <summary>
    /// Sends an mDNS PTR query for "_beo4._tcp.local" on every IPv4 interface and collects the
    /// SRV records of the replies. Sent from an ephemeral port, so responders answer unicast.
    /// </summary>
    public static async Task<List<DeviceInfo>> ScanDevices(CancellationToken ct, Action<StatusMessage>? status = null)
    {
        status?.Invoke(new DeviceStatusMessage(DeviceStatus.Discovering, "○ Searching WiFi..."));

        var found = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);   // key = host
        var query = BuildPtrQuery(ServiceName);
        var group = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        foreach (var local in LocalIPv4Addresses())
        {
            try
            {
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                await udp.SendAsync(query, group, ct);
            }
            catch (SocketException) { /* interface not usable for multicast */ }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ScanDuration);
        try
        {
            while (true)
            {
                var reply = await udp.ReceiveAsync(timeout.Token);
                try
                {
                    foreach (var (instance, host) in ParseSrvRecords(reply.Buffer))
                        found[host] = new DeviceInfo(DeviceType.WIFI, instance, host);
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException) { /* malformed packet */ }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* scan time over */ }

        return found.Values.ToList();
    }

    /// <summary>
    /// IPv4 address of an mDNS host name (e.g. "beo4remote-atoms3.local") by an A query to 224.0.0.251,
    /// sent from an ephemeral port so the device answers unicast; null when nobody answers within 2 s.
    /// </summary>
    public static async Task<IPAddress?> ResolveMdnsAsync(string host, CancellationToken ct)
    {
        var query = BuildQuery(host, type: 1);                                  // A
        var group = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        foreach (var local in LocalIPv4Addresses())
        {
            try
            {
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                await udp.SendAsync(query, group, ct);
            }
            catch (SocketException) { /* interface not usable for multicast */ }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ScanDuration);
        try
        {
            while (true)
            {
                var reply = await udp.ReceiveAsync(timeout.Token);
                try
                {
                    if (ParseARecord(reply.Buffer, host) is { } address)
                        return address;
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException) { /* malformed packet */ }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* nobody answered */ }
        return null;
    }

    /// <summary>The IPv4 address of <paramref name="host"/> from the A records of a DNS packet, or null.</summary>
    private static IPAddress? ParseARecord(byte[] b, string host)
    {
        int questions = b[4] << 8 | b[5];
        int records   = (b[6] << 8 | b[7]) + (b[8] << 8 | b[9]) + (b[10] << 8 | b[11]);
        int pos = 12;
        for (int i = 0; i < questions; i++) { ReadName(b, ref pos); pos += 4; }
        for (int i = 0; i < records; i++)
        {
            var name  = ReadName(b, ref pos);
            int type  = b[pos] << 8 | b[pos + 1];
            int rdLen = b[pos + 8] << 8 | b[pos + 9];
            pos += 10;
            if (type == 1 && rdLen == 4 && name.Equals(host, StringComparison.OrdinalIgnoreCase))
                return new IPAddress(b.AsSpan(pos, 4));
            pos += rdLen;
        }
        return null;
    }

    private static IEnumerable<IPAddress> LocalIPv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address);

    private static byte[] BuildPtrQuery(string name) => BuildQuery(name, type: 12);   // PTR

    private static byte[] BuildQuery(string name, byte type)
    {
        var packet = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };   // id 0, flags 0, 1 question
        foreach (var label in name.Split('.'))
        {
            packet.Add((byte)label.Length);
            packet.AddRange(Encoding.ASCII.GetBytes(label));
        }
        packet.AddRange(new byte[] { 0, 0, type, 0, 1 });                      // end, type, class IN
        return packet.ToArray();
    }

    /// <summary>Yields (instance label, target host) for every SRV record of our service in a DNS packet.</summary>
    private static IEnumerable<(string Instance, string Host)> ParseSrvRecords(byte[] b)
    {
        int questions = b[4] << 8 | b[5];
        int records   = (b[6] << 8 | b[7]) + (b[8] << 8 | b[9]) + (b[10] << 8 | b[11]);
        int pos = 12;
        for (int i = 0; i < questions; i++) { ReadName(b, ref pos); pos += 4; }
        for (int i = 0; i < records; i++)
        {
            var name  = ReadName(b, ref pos);
            int type  = b[pos] << 8 | b[pos + 1];
            int rdLen = b[pos + 8] << 8 | b[pos + 9];
            pos += 10;
            if (type == 33 && name.EndsWith("." + ServiceName, StringComparison.OrdinalIgnoreCase))
            {
                int p = pos + 6;                                                // skip priority, weight, port
                yield return (name[..^(ServiceName.Length + 1)], ReadName(b, ref p));
            }
            pos += rdLen;
        }
    }

    /// <summary>Reads a (possibly compressed) DNS name at <paramref name="pos"/> and advances past it.</summary>
    private static string ReadName(byte[] b, ref int pos)
    {
        var labels = new List<string>();
        int p = pos, jumps = 0;
        bool jumped = false;
        while (b[p] != 0)
        {
            if ((b[p] & 0xC0) == 0xC0)
            {
                if (!jumped) pos = p + 2;
                jumped = true;
                if (++jumps > 20) throw new ArgumentException("DNS name loop");
                p = (b[p] & 0x3F) << 8 | b[p + 1];
                continue;
            }
            labels.Add(Encoding.UTF8.GetString(b, p + 1, b[p]));
            p += b[p] + 1;
        }
        if (!jumped) pos = p + 1;
        return string.Join('.', labels);
    }

    public void CancelAutoDetect() => _cts?.Cancel();

    public void Disconnect()
    {
        Cleanup();
        OnStatusChanged?.Invoke(new DeviceStatusMessage(DeviceStatus.Disconnected, "not connected"));
    }

    public void SendLine(string line)
    {
        if (_writer is null || !IsConnected)
        {
            OnLog?.Invoke(new LogMessage(LogLevel.Error, "Not connected via WiFi."));
            return;
        }
        Task.Run(async () =>
        {
            try
            {
                await _writer.WriteLineAsync(line);
                await _writer.WriteLineAsync("status");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke(new LogMessage(LogLevel.Error, $"WiFi send error: {ex.Message}"));
            }
        });
    }

    public void Dispose() => Disconnect();

    private void StartReadLoop(CancellationToken ct)
    {
        var reader = _reader!;
        Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } raw)
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0) continue;
                    if (ProtocolStatusParser.TryParseSourceStatus(line, out var source))
                        OnStatusChanged?.Invoke(new SourceStatusMessage(source));
                    else
                        OnLog?.Invoke(new LogMessage(LogLevel.Debug, line));
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { OnLog?.Invoke(new LogMessage(LogLevel.Error, $"WiFi read error: {ex.Message}")); }

            // remote side closed the connection (or read failed)
            if (!ct.IsCancellationRequested)
            {
                Cleanup();
                OnStatusChanged?.Invoke(new DeviceStatusMessage(DeviceStatus.Disconnected, "WiFi disconnected"));
            }
        });
    }

    private void Cleanup()
    {
        _cts?.Cancel();
        _cts = null;
        _client?.Dispose();
        _client = null;
        _reader = null;
        _writer = null;
    }
}
