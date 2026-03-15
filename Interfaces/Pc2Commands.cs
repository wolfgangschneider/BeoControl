namespace BeoControl.Interfaces;

/// <summary>
/// PC2-only commands not available on Beo4 IR.
/// Commands shared with Beo4 (tv, cd, allstandby, doorcam, etc.) live in Beo4Commands.All.
/// </summary>
public static class Pc2Commands
{
    public static readonly CommandInfo[] All =
    [
        // ── PC2-only sources (no known Beo4 IR equivalent yet) ────────────
        new("dvd2",     "DVD 2",          "PC2 Source"),

        // ── Incremental tone controls (PC2 hardware mixer only) ───────────
        new("bass+",    "Bass up",        "PC2 Tone", "[steps]"),
        new("bass-",    "Bass down",      "PC2 Tone", "[steps]"),
        new("treble+",  "Treble up",      "PC2 Tone", "[steps]"),
        new("treble-",  "Treble down",    "PC2 Tone", "[steps]"),
        new("balance+", "Balance right",  "PC2 Tone", "[steps]"),
        new("balance-", "Balance left",   "PC2 Tone", "[steps]"),
    ];
}
