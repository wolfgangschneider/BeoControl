using Beoported.Pc2;

using System.Text.RegularExpressions;

namespace Pc2Adapter;

public static partial class Pc2AudioStatusParser
{
    [GeneratedRegex(
        @"^vol=(?<vol>\d+)\s+bass=(?<bass>[+-]?\d+)\s+treble=(?<treble>[+-]?\d+)\s+balance=(?<balance>[+-]?\d+)\s+loudness=(?<loudness>on|off)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudioStatusRegex();

    public static bool TryParse(string text, out AudioSetup setup)
    {
        var match = AudioStatusRegex().Match(text.Trim());
        if (!match.Success)
        {
            setup = default!;
            return false;
        }

        if (!byte.TryParse(match.Groups["vol"].Value, out var volume) ||
            !sbyte.TryParse(match.Groups["bass"].Value, out var bass) ||
            !sbyte.TryParse(match.Groups["treble"].Value, out var treble) ||
            !sbyte.TryParse(match.Groups["balance"].Value, out var balance))
        {
            setup = default!;
            return false;
        }

        setup = new AudioSetup
        {
            Volume = volume,
            Bass = bass,
            Treble = treble,
            Balance = balance,
            Loudness = string.Equals(match.Groups["loudness"].Value, "on", StringComparison.OrdinalIgnoreCase),
        };
        return true;
    }
}
