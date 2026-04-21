namespace BeoControl.Interfaces;

/// <summary>
/// PC2-only commands not available on Beo4 IR.
/// Commands shared with Beo4 (tv, cd, allstandby, doorcam, etc.) live in Beo4Commands.All.
/// </summary>
public static class Pc2Commands
{
    public static readonly BeoCommand[] All =
    [
        // ── PC2-only sources (no known Beo4 IR equivalent yet) ────────────
        BeoCommands.Get(CommandId.Pc2Dvd2),
        BeoCommands.Get(CommandId.Pc2On),

        // ── Incremental tone controls (PC2 hardware mixer only) ───────────
        BeoCommands.Get(CommandId.Pc2BassUp),
        BeoCommands.Get(CommandId.Pc2BassDown),
        BeoCommands.Get(CommandId.Pc2TrebleUp),
        BeoCommands.Get(CommandId.Pc2TrebleDown),
        BeoCommands.Get(CommandId.Pc2BalanceUp),
        BeoCommands.Get(CommandId.Pc2BalanceDown),
    ];
}
