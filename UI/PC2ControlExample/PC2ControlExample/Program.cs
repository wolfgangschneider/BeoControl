using BeoControl.Interfaces;
using Pc2Adapter;

Console.WriteLine("=== PC2 Control Example ===");
Console.WriteLine("Connecting to PC2...");

using var device = new Pc2Device();

device.OnLog           += msg => Console.WriteLine($"[{msg.Level}] {msg.Text}");
device.OnStatusChanged += msg => Console.WriteLine($"  {msg.Text}");

await device.Connect();

if (!device.IsConnected)
{
    Console.WriteLine("Failed to connect. Is the PC2 USB cable plugged in?");
    return;
}

Console.WriteLine();
Console.WriteLine("Commands: cd | radio | vol+ | vol- | exit");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine()?.Trim().ToLowerInvariant();

    switch (input)
    {
        case "cd":    device.SendCommand("CD");         break;
        case "radio": device.SendCommand("RADIO");      break;
        case "vol+":  device.SendCommand("VOL",  "1");  break;
        case "vol-":  device.SendCommand("VOL", "-1");  break;
        case "exit":
        case "quit":
        case "q":
            Console.WriteLine("Disconnecting...");
            device.Disconnect();
            return;
        default:
            Console.WriteLine("Unknown command. Try: cd | radio | vol+ | vol- | exit");
            break;
    }
}

