using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Beo4Adapter.Firmware;

/// <summary>
/// Firmware upload to an ESP32 running ArduinoOTA (same protocol as PlatformIO's espota.py, no password):
/// UDP invitation to port 3232 → device answers "OK" → device connects back to our TCP listener →
/// we stream the image, the device acks each chunk and sends "OK" after the image was verified and written.
/// </summary>
public static class EspOta
{
    private const int OtaPort = 3232;
    private const int ChunkSize = 1460;
    private const int CommandFlash = 0;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FinishTimeout = TimeSpan.FromSeconds(60);

    /// <param name="host">Device host name (e.g. "beo4remote-atom.local") or IP.</param>
    /// <param name="progress">Percent 0..100.</param>
    public static async Task UploadAsync(string host, byte[] image, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        // ".local" via mDNS first: Android can't resolve it with the system resolver
        var address = (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                          ? await Transport.TcpTransport.ResolveMdnsAsync(host, ct) : null)
            ?? (await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct)).FirstOrDefault()
            ?? throw new Exception($"Cannot resolve {host}.");

        using var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var localPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        // 1. invitation: "<command> <local port> <size> <md5>\n"
        var md5 = Convert.ToHexString(MD5.HashData(image)).ToLowerInvariant();
        var invitation = Encoding.ASCII.GetBytes($"{CommandFlash} {localPort} {image.Length} {md5}\n");
        using (var udp = new UdpClient(AddressFamily.InterNetwork))
        {
            await udp.SendAsync(invitation, new IPEndPoint(address, OtaPort), ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            var reply = Encoding.ASCII.GetString((await udp.ReceiveAsync(timeout.Token)).Buffer).Trim();
            if (reply != "OK")
                throw new Exception(reply.StartsWith("AUTH") ? "Device requires an OTA password." : $"Device refused OTA: {reply}");
        }

        // 2. device connects back
        using var accept = CancellationTokenSource.CreateLinkedTokenSource(ct);
        accept.CancelAfter(Timeout);
        using var client = await listener.AcceptTcpClientAsync(accept.Token);
        var stream = client.GetStream();
        var ack = new byte[32];
        var received = new StringBuilder();

        // 3. stream the image, device answers each chunk with the byte count written so far
        for (var offset = 0; offset < image.Length; offset += ChunkSize)
        {
            var length = Math.Min(ChunkSize, image.Length - offset);
            using var chunk = CancellationTokenSource.CreateLinkedTokenSource(ct);
            chunk.CancelAfter(Timeout);
            await stream.WriteAsync(image.AsMemory(offset, length), chunk.Token);
            var n = await stream.ReadAsync(ack, chunk.Token);
            if (n == 0)
                throw new Exception("Device closed the connection during upload.");
            received.Clear().Append(Encoding.ASCII.GetString(ack, 0, n));   // last ack may already carry the final "OK"
            progress?.Report((offset + length) * 100 / image.Length);
        }

        // 4. final "OK" after Update.end() (MD5 checked on the device), then it reboots
        using var finish = CancellationTokenSource.CreateLinkedTokenSource(ct);
        finish.CancelAfter(FinishTimeout);
        while (!received.ToString().Contains("OK"))
        {
            var n = await stream.ReadAsync(ack, finish.Token);
            if (n == 0)
                throw new Exception("Device closed the connection before confirming the update.");
            received.Append(Encoding.ASCII.GetString(ack, 0, n));
        }
    }
}
