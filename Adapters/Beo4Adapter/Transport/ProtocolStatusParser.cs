namespace Beo4Adapter.Transport;

internal static class ProtocolStatusParser
{
    private const string TaggedSourcePrefix = "SRC:";

    public static bool TryParseSourceStatus(string line, out string statusText)
    {
        if (line.StartsWith(TaggedSourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            statusText = line.Replace(TaggedSourcePrefix, "").Trim();
            return true;
        }

        statusText = string.Empty;
        return false;
    }
}
