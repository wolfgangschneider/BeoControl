using Beoported.Masterlink;

namespace Beoported.Logging;

/// <summary>
/// Console logging helpers: timestamped hex rows with ANSI coloring.
/// Yellow for sent, default for received, red for unknown.
/// </summary>
public static class ConsoleLog
{

    private static string NowString()
    {
        var now = DateTimeOffset.Now;
        return now.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
    }

    /// <summary>Format a hex row with timestamp and label. Yellow if sent.</summary>
    public static string FormatHexRow(ReadOnlySpan<byte> data, string label, bool isSend)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(isSend ? "\x1b[1;33m" : "\x1b[0m");
        sb.Append($"[{NowString()}] {label}");
        foreach (var b in data)
            sb.Append($" {b:X2}");
        if (isSend) sb.Append("\x1b[0m");
        return sb.ToString();
    }

    /// <summary>Format an unknown message type in bold red.</summary>
    public static string FormatUnknownMessage(byte[] tgram, byte msgType)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"\x1b[1;31m[{NowString()}] UNKNOWN MSG type=0x{msgType:X2} =>");
        foreach (var b in tgram)
            sb.Append($" {b:X2}");
        sb.Append("\x1b[0m");
        return sb.ToString();
    }

}
