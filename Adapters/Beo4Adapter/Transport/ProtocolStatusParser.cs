namespace Beo4Adapter.Transport;

internal static class ProtocolStatusParser
{
    private const string TaggedSourcePrefix = "SRC:";
    private const string LegacySourcePrefix = "Current source:";

    public static bool TryParseSourceStatus(string line, out string statusText)
    {
        if (line.StartsWith(TaggedSourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var source = line[TaggedSourcePrefix.Length..].Trim();
            statusText = $"{LegacySourcePrefix} {source}".TrimEnd();
            return true;
        }

        if (line.StartsWith(LegacySourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            statusText = line;
            return true;
        }

        statusText = string.Empty;
        return false;
    }
}
