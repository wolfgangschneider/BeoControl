using BeoControl.Interfaces;

namespace BeoControl;

/// <summary>App-level slash commands (not B&amp;O device commands).</summary>
public static class AppCommands
{
    public static readonly CommandInfo[] SlashCommands =
    [
        new("/help",   "Show all commands",              "App Commands"),
        new("/clear",  "Clear the log",                  "App Commands"),
        new("/debug",  "Toggle debug log messages",      "App Commands"),
        new("/exit",   "Exit the application",           "App Commands"),
        new("/port",       "Connect via serial/USB",         "App Commands", "[COMx] | scan]"),
        new("/port scan",  "Scan and list serial devices",   "App Commands"),
        new("/bt",         "Connect via Bluetooth (BLE)",    "App Commands", "[scan]"),
        new("/bt scan",    "Scan and list BLE devices",      "App Commands"),
        new("/bt-last",    "Reconnect last BLE device",      "App Commands"),
        new("/pc2",        "Connect to PC2 USB device",      "App Commands"),
    ];
}