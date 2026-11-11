using Beo4Adapter.Firmware;

// EXPERIMENTAL: EspFlashTest <COMx> <image.bin> [offset, default 0x0] [baud, default 460800]
if (args.Length < 2)
{
    Console.WriteLine("usage: EspFlashTest <COMx> <image.bin> [offset=0x0] [baud=460800]");
    return 1;
}

var image = File.ReadAllBytes(args[1]);
var offset = args.Length > 2 ? Convert.ToUInt32(args[2], 16) : 0u;
var baud = args.Length > 3 ? int.Parse(args[3]) : 460800;

Console.WriteLine($"Flashing {image.Length} bytes @ 0x{offset:x} via {args[0]} ({baud} baud)…");
var progress = new Progress<int>(p => Console.Write($"\r{p,3} %"));
var chip = await EspFlasher.FlashAsync(args[0], image, offset, progress, baud);
Console.WriteLine($"\nDone, chip {chip}, board reset.");
return 0;
