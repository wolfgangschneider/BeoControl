using System.Buffers.Binary;
using System.IO.Ports;

namespace Beo4Adapter.Firmware;

/// <summary>
/// EXPERIMENTAL — flashes an image over USB/serial through the ESP32 ROM bootloader
/// (no RAM stub, like <c>esptool --no-stub</c>). Meant for the first install of a factory image
/// (bootloader + partitions + app merged, written at offset 0).
/// Ported from esputil by Cesanta (https://github.com/cpq/esputil, MIT license).
/// </summary>
public static class EspFlasher
{
    // SLIP framing (RFC 1055)
    private const byte End = 0xC0, Esc = 0xDB, EscEnd = 0xDC, EscEsc = 0xDD;

    // ROM loader commands
    private const byte OpFlashBegin = 2, OpFlashData = 3, OpSync = 8, OpReadReg = 10,
                       OpSpiSetParams = 11, OpSpiAttach = 13, OpChangeBaud = 15;

    private const uint ChipIdAddress = 0x40001000;
    private const int BlockSize = 4096;

    private static readonly Dictionary<uint, string> KnownChips = new()
    {
        [0x00f01d83] = "ESP32",
        [0x00000009] = "ESP32-S3",
        [0x000007c6] = "ESP32-S2",
        [0x1b31506f] = "ESP32-C3",
        [0x6921506f] = "ESP32-C3",
    };

    // chip id in the image header (esp_image_header_t.chip_id) per chip name
    private static readonly Dictionary<string, ushort> ImageChipIds = new()
    {
        ["ESP32"] = 0, ["ESP32-S2"] = 2, ["ESP32-C3"] = 5, ["ESP32-S3"] = 9,
    };

    /// <summary>Resets the board into the ROM bootloader, writes <paramref name="image"/> at <paramref name="offset"/> and resets it.</summary>
    /// <returns>Detected chip name, e.g. "ESP32-S3".</returns>
    public static Task<string> FlashAsync(string port, byte[] image, uint offset = 0, IProgress<int>? progress = null,
                                          int baud = 460800, CancellationToken ct = default)
        => Task.Run(() => Flash(port, image, offset, progress, baud, ct), ct);

    private static string Flash(string port, byte[] image, uint offset, IProgress<int>? progress, int baud, CancellationToken ct)
    {
        using var sp = new SerialPort(port, 115200) { ReadTimeout = 20, WriteTimeout = 2000, DtrEnable = false, RtsEnable = false };
        sp.Open();
        var link = new Link(sp);

        var (chipId, chip) = Connect(link, ct)
            ?? throw new Exception("No ESP32 bootloader answered. Hold the boot button, press reset, release boot and retry.");
        var esp32Classic = chipId == 0x00f01d83;

        // factory images start with the bootloader (0x0, ESP32 classic 0x1000), app images at offset 0x10000 with the app
        var header = offset == 0 && image.Length > 0x1000 && image[0] != 0xE9 ? 0x1000 : 0;
        if (image.Length < header + 14 || image[header] != 0xE9)
            throw new Exception("Not an ESP32 image.");
        var imageChip = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(header + 12));
        if (ImageChipIds[chip] != imageChip)
            throw new Exception($"Image is for another chip (id {imageChip}), the board is an {chip}. Nothing was written.");

        if (baud > 115200)
        {
            link.Command(OpChangeBaud, U32(baud, 0), 0, 100);
            sp.BaudRate = baud;
            Thread.Sleep(50);
            sp.DiscardInBuffer();
        }

        // SPI attach is mandatory on ESP32-class chips: default pins, then a generic 4 MB geometry
        link.Command(OpSpiAttach, U32(0, 0), 0, 250);
        link.Command(OpSpiSetParams, U32(0, 4 * 1024 * 1024, 65536, 4096, 256, 0xffff), 0, 250);

        // FLASH_BEGIN erases the target area; newer chips (S2/S3/C3) take a 5th "encrypted" word
        var blocks = (image.Length + BlockSize - 1) / BlockSize;
        var begin = esp32Classic
            ? U32(image.Length, blocks, BlockSize, (int)offset)
            : U32(image.Length, blocks, BlockSize, (int)offset, 0);
        var eraseTimeout = Math.Max(15000, image.Length / (1024 * 1024) * 30000 + 15000);
        link.Command(OpFlashBegin, begin, 0, eraseTimeout);

        var packet = new byte[16 + BlockSize];
        for (var seq = 0; seq < blocks; seq++)
        {
            ct.ThrowIfCancellationRequested();
            var start = seq * BlockSize;
            var n = Math.Min(BlockSize, image.Length - start);
            Array.Fill(packet, (byte)0xFF, 16, BlockSize);                 // last block padded like esptool
            Array.Copy(image, start, packet, 16, n);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0), BlockSize);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), seq);
            BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(8), 0);
            link.Command(OpFlashData, packet, Checksum(packet.AsSpan(16)), 3000);
            progress?.Report((seq + 1) * 100 / blocks);
        }

        // hard reset: EN low with IO0 high → boots the new firmware
        sp.DtrEnable = false;
        sp.RtsEnable = true;
        Thread.Sleep(100);
        sp.RtsEnable = false;
        return chip;
    }

    /// <summary>Alternates the classic DTR/RTS auto-reset and the USB-JTAG-serial sequence, then SYNCs and reads the chip id.</summary>
    private static (uint Id, string Name)? Connect(Link link, CancellationToken ct)
    {
        var sync = new byte[36];
        sync[0] = 7; sync[1] = 7; sync[2] = 0x12; sync[3] = 0x20;
        Array.Fill(sync, (byte)0x55, 4, 32);

        for (var attempt = 0; attempt < 6; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if ((attempt & 1) != 0) ResetUsbJtag(link.Port);
            else ResetClassic(link.Port);
            link.Port.DiscardInBuffer();

            for (var i = 0; i < 2 + attempt; i++)
            {
                if (link.TryCommand(OpSync, sync, 0, 100) is null)
                    continue;
                Thread.Sleep(50);
                link.Port.DiscardInBuffer();                                  // further SYNC replies
                link.ChipKnown = true;
                var reply = link.Command(OpReadReg, U32(unchecked((int)ChipIdAddress)), 0, 200);
                var id = BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(4));
                return KnownChips.TryGetValue(id, out var name)
                    ? (id, name)
                    : throw new Exception($"Unknown chip id 0x{id:x8}.");
            }
        }
        return null;
    }

    // DTR drives IO0 (inverted), RTS drives EN (inverted) on the usual auto-reset circuit
    private static void ResetClassic(SerialPort sp)
    {
        Thread.Sleep(100);
        sp.DtrEnable = false; sp.RtsEnable = true;     // IO0 high, EN low
        Thread.Sleep(100);
        sp.DtrEnable = true; sp.RtsEnable = false;     // IO0 low, EN high → ROM download mode
        Thread.Sleep(50);
        sp.DtrEnable = false;                          // IO0 high
    }

    private static void ResetUsbJtag(SerialPort sp)
    {
        sp.RtsEnable = false; sp.DtrEnable = false;
        Thread.Sleep(100);
        sp.DtrEnable = true; sp.RtsEnable = false;
        Thread.Sleep(100);
        sp.RtsEnable = true; sp.DtrEnable = false; sp.RtsEnable = true;
        Thread.Sleep(100);
        sp.DtrEnable = false; sp.RtsEnable = false;
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        byte v = 0xEF;
        foreach (var b in data) v ^= b;
        return v;
    }

    private static byte[] U32(params int[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    /// <summary>SLIP-framed request/response on the serial port.</summary>
    private sealed class Link(SerialPort port)
    {
        public SerialPort Port { get; } = port;

        /// <summary>Until the chip is known, the status sits 2 bytes before the end (as esputil does), afterwards 4.</summary>
        public bool ChipKnown { get; set; }

        public byte[] Command(byte op, byte[] data, uint checksum, int timeoutMs)
            => TryCommand(op, data, checksum, timeoutMs) ?? throw new TimeoutException($"No reply to bootloader command {op}.");

        /// <summary>Returns the reply frame, null on timeout; throws when the ROM reports an error.</summary>
        public byte[]? TryCommand(byte op, byte[] data, uint checksum, int timeoutMs)
        {
            var request = new byte[8 + data.Length];
            request[1] = op;
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(2), (ushort)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), checksum);
            data.CopyTo(request, 8);
            var encoded = SlipEncode(request);
            Port.Write(encoded, 0, encoded.Length);

            var frame = new List<byte>();
            bool inFrame = false, escaped = false;
            var deadline = Environment.TickCount64 + timeoutMs;
            var buffer = new byte[4096];
            while (Environment.TickCount64 < deadline)
            {
                int n;
                try { n = Port.Read(buffer, 0, buffer.Length); }
                catch (TimeoutException) { continue; }

                for (var i = 0; i < n; i++)
                {
                    var c = buffer[i];
                    if (c == End)
                    {
                        // END delimits frames; anything between frames (boot log text) fails the checks below
                        if (inFrame && frame.Count >= 10 && frame[0] == 1 && frame[1] == op)
                        {
                            var reply = frame.ToArray();
                            var status = ChipKnown ? reply.Length - 4 : reply.Length - 2;
                            if (reply[status] != 0)
                                throw new Exception($"Bootloader command {op} failed, error {reply[status + 1]}.");
                            return reply;
                        }
                        inFrame = true;
                        frame.Clear();
                        escaped = false;
                        continue;
                    }
                    if (!inFrame) continue;
                    if (escaped) { frame.Add(c == EscEnd ? End : c == EscEsc ? Esc : c); escaped = false; }
                    else if (c == Esc) escaped = true;
                    else frame.Add(c);
                }
            }
            return null;
        }

        private static byte[] SlipEncode(byte[] data)
        {
            var output = new List<byte>(data.Length + 16) { End };
            foreach (var b in data)
            {
                if (b == End) { output.Add(Esc); output.Add(EscEnd); }
                else if (b == Esc) { output.Add(Esc); output.Add(EscEsc); }
                else output.Add(b);
            }
            output.Add(End);
            return output.ToArray();
        }
    }
}
