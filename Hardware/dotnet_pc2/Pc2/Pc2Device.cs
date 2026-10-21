using Beoported.Logging;

using LibUsbDotNet;
using LibUsbDotNet.Main;
using LibUsbDotNet.WinUsb;

using Microsoft.Win32;

using System.Collections.Concurrent;

namespace Beoported.Pc2;

/// <summary>
/// Low-level PC2 USB I/O: open, read (with reassembly), write (with framing and labels).
/// </summary>
public sealed class Pc2Device : IDisposable
{
    private const int VendorId = 0x0CD4;
    private const int ProductId = 0x0101;
    private const byte EndpointIn = 0x81; // LIBUSB_ENDPOINT_IN | 1
    private const byte EndpointOut = 0x01; // OUT endpoint

    private UsbDevice? _usbDevice;
    private UsbEndpointReader? _reader;
    private UsbEndpointWriter? _writer;
    private readonly object _writeLock = new();
    private volatile bool _disposed;

    /// <summary>Receives formatted debug text for every sent and received USB frame.</summary>
    internal Action<string>? DebugLog { get; set; }
    private readonly BlockingCollection<byte[]> _inbox = new();
    // Bytes read from USB that don't form a complete frame yet (see ExtractFrames).
    private readonly List<byte> _rxBuffer = new();
    private ulong _rxSeq;

    /// <summary>Open the PC2 USB device.</summary>
    public void Open()
    {
        var finder = new UsbDeviceFinder(VendorId, ProductId);
        _usbDevice = UsbDevice.OpenUsbDevice(finder) ?? TryOpenWinUsbDeviceBySymbolicName()
            ?? throw new InvalidOperationException("PC2 device not found (VID=0x0CD4, PID=0x0101)");

        // For Linux libusb: claim interface 0
        if (_usbDevice is IUsbDevice wholeDevice)
        {
            wholeDevice.SetConfiguration(1);
            wholeDevice.ClaimInterface(0);
        }

        _reader = _usbDevice.OpenEndpointReader((ReadEndpointID)EndpointIn);
        _writer = _usbDevice.OpenEndpointWriter((WriteEndpointID)EndpointOut);

        // Start background read thread
        var readThread = new Thread(ReadLoop) { IsBackground = true, Name = "PC2-USB-Read" };
        readThread.Start();
    }

    private UsbDevice? TryOpenWinUsbDeviceBySymbolicName()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        using var vidPidKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\USB\VID_{VendorId:X4}&PID_{ProductId:X4}");
        if (vidPidKey is null)
            return null;

        foreach (var instanceName in vidPidKey.GetSubKeyNames())
        {
            using var deviceParametersKey = vidPidKey.OpenSubKey($@"{instanceName}\Device Parameters");
            var symbolicName = deviceParametersKey?.GetValue("SymbolicName") as string;
            if (string.IsNullOrWhiteSpace(symbolicName))
                continue;

            if (WinUsbDevice.Open(symbolicName, out WinUsbDevice winUsbDevice))
            {
                DebugLog?.Invoke($"PC2: opened via WinUSB symbolic name fallback ({symbolicName}).");
                return winUsbDevice;
            }
        }

        return null;
    }

    /// <summary>Send a raw PC2 message (will be wrapped in 0x60..0x61 frame).</summary>
    public void SendMessage(ReadOnlySpan<byte> message)
    {
        if (_disposed) return;
        // Build frame: [0x60] [length] [message...] [0x61]
        var frame = new byte[message.Length + 3];
        frame[0] = 0x60;
        frame[1] = (byte)message.Length;
        message.CopyTo(frame.AsSpan(2));
        frame[^1] = 0x61;

        // Label based on command byte
        string label = message.Length > 0 ? message[0] switch
        {
            0xF1 => "Send [INIT] =>",
            0x80 => "Send [SET_NODE] =>",
            0xF6 => "Send [ADDR_FILTER] =>",
            0x24 => "Send [LOCK] =>",
            0xE4 => "Send [UNLOCK] =>",
            0xA7 => "Send [SHUTDOWN] =>",
            0xE0 => "Send [ML_TELEGRAM] =>",
            0x12 => "Send [BEO4_KEY] =>",
            0xE3 => "Send [MIXER_PARAMS] =>",
            0xE5 => "Send [AUDIO_ROUTE] =>",
            0xE7 => "Send [MUTE] =>",
            0xEA => "Send [SPEAKER] =>",
            0xEB => "Send [VOLUME] =>",
            _ => "Send =>",
        } : "Send =>";

        DebugLog?.Invoke(ConsoleLog.FormatHexRow(frame, label, isSend: true));

        lock (_writeLock)
        {
            var writer = _writer ?? throw new InvalidOperationException("PC2 USB writer not initialized.");
            writer.Write(frame, 5000, out _);
        }
    }

    /// <summary>Set the PC2 address filter.</summary>
    public void SetAddressFilter(AddressMask mask)
    {
        switch (mask)
        {
            case AddressMask.AudioMaster:
                SendMessage([0xF6, 0x10, 0xC1, 0x80, 0x83, 0x05, 0x00, 0x00]);
                break;
            case AddressMask.Beoport:
                SendMessage([0xF6, 0x00, 0x82, 0x80, 0x83]);
                break;
            case AddressMask.Promiscuous:
                SendMessage([0xF6]);
                break;
        }
    }

    /// <summary>Set PC2 option mode via SET_NODE (0x80 0x01 [option]).</summary>
    public void SetNodeOption(byte option)
    {

        SendMessage([0x80, 0x01, option]);
    }

    /// <summary>Read the next complete message (blocking).</summary>
    public byte[] Read() => _inbox.Take();

    /// <summary>Try to read the next message with timeout.</summary>
    public bool TryRead(out byte[] message, int timeoutMs)
        => _inbox.TryTake(out message!, timeoutMs);

    private void ReadLoop()
    {
        var buffer = new byte[1024];
        while (!_disposed)
        {
            try
            {
                var ec = _reader!.Read(buffer, 1000, out int bytesRead);
                if (_disposed) break;
                if (ec != ErrorCode.None && ec != ErrorCode.IoTimedOut)
                    continue;
                if (bytesRead == 0)
                    continue;

                var raw = buffer[..bytesRead];
                DebugLog?.Invoke(ConsoleLog.FormatHexRow(raw, $"Recv raw #{_rxSeq++} =>", isSend: false));

                _rxBuffer.AddRange(raw);
                var frames = ExtractFrames(_rxBuffer, out int skipped);
                foreach (var frame in frames)
                {
                    _inbox.Add(frame);
                    _rxSeq = 0;
                }
                if (skipped > 0)
                    DebugLog?.Invoke($"PC2: skipped {skipped} byte(s) outside a frame");
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception) when (_disposed) { break; }
        }
    }

    /// <summary>
    /// Cut complete frames <c>[0x60] [len] [len bytes] [0x61]</c> out of the receive buffer.
    /// A USB read can hold part of a frame, several frames, or bytes that belong to none
    /// (e.g. left over in the PC2 after the Mac restarted). Bytes before a frame start, and
    /// a start whose end byte doesn't match, are dropped one by one until a valid frame
    /// lines up again -- so one stray byte can't throw off every following frame.
    /// </summary>
    // Longest frame payload accepted. The longest seen are Masterlink telegrams of ~0x30
    // bytes; a larger length byte means we're not at a frame start.
    private const int MaxFrameData = 0x80;

    internal static List<byte[]> ExtractFrames(List<byte> buf, out int skipped)
    {
        var frames = new List<byte[]>();
        skipped = 0;
        while (true)
        {
            int start = buf.IndexOf(0x60);
            if (start < 0)
            {
                skipped += buf.Count;
                buf.Clear();
                break;
            }
            if (start > 0)
            {
                skipped += start;
                buf.RemoveRange(0, start);
            }
            if (buf.Count < 2)
                break;                              // length byte not here yet
            if (buf[1] > MaxFrameData)
            {
                skipped += 1;                       // no PC2 frame is that long
                buf.RemoveAt(0);
                continue;
            }
            int frameLength = buf[1] + 3;
            if (buf.Count < frameLength)
                break;                              // rest of the frame not here yet
            if (buf[frameLength - 1] != 0x61)
            {
                skipped += 1;                       // not a frame start after all
                buf.RemoveAt(0);
                continue;
            }
            frames.Add(buf.GetRange(0, frameLength).ToArray());
            buf.RemoveRange(0, frameLength);
        }
        return frames;
    }

    public void Dispose()
    {
        _disposed = true;
        _reader?.Abort();
        if (_usbDevice is IUsbDevice wholeDevice)
            wholeDevice.ReleaseInterface(0);
        _usbDevice?.Close();
        _inbox.Dispose();
    }
}
