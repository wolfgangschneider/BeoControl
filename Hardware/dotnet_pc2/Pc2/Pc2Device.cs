using System.Collections.Concurrent;
using Beoported.Logging;
using Beoported.Masterlink;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace Beoported.Pc2;

/// <summary>
/// Low-level PC2 USB I/O: open, read (with reassembly), write (with framing and labels).
/// </summary>
public sealed class Pc2Device : IDisposable
{
    private const int VendorId  = 0x0CD4;
    private const int ProductId = 0x0101;
    private const byte EndpointIn  = 0x81; // LIBUSB_ENDPOINT_IN | 1
    private const byte EndpointOut = 0x01; // OUT endpoint

    private UsbDevice? _usbDevice;
    private UsbEndpointReader? _reader;
    private UsbEndpointWriter? _writer;
    private readonly object _writeLock = new();
    private volatile bool _disposed;

    /// <summary>Receives formatted debug text for every sent and received USB frame.</summary>
    internal Action<string>? DebugLog { get; set; }
    private readonly BlockingCollection<byte[]> _inbox = new();
    private byte[] _reassemblyBuffer = [];
    private int _remainingBytes;
    private ulong _rxSeq;

    /// <summary>Open the PC2 USB device.</summary>
    public void Open()
    {
        var finder = new UsbDeviceFinder(VendorId, ProductId);
        _usbDevice = UsbDevice.OpenUsbDevice(finder)
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
            _    => "Send =>",
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

                if (_reassemblyBuffer.Length > 0)
                {
                    _reassemblyBuffer = [.. _reassemblyBuffer, .. raw];
                    _remainingBytes -= bytesRead;
                    if (_remainingBytes <= 0)
                    {
                        _inbox.Add(_reassemblyBuffer);
                        _reassemblyBuffer = [];
                        _rxSeq = 0;
                    }
                }
                else
                {
                    if (bytesRead < 3) continue;
                    int msgLength = raw[1] + 3;
                    if (msgLength > bytesRead)
                    {
                        _reassemblyBuffer = raw.ToArray();
                        _remainingBytes = msgLength - bytesRead;
                    }
                    else
                    {
                        _inbox.Add(raw.ToArray());
                        _rxSeq = 0;
                    }
                }
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception) when (_disposed) { break; }
        }
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
