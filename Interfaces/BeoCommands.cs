namespace BeoControl.Interfaces;

public static class BeoCommands
{
    private static readonly Dictionary<CommandId, BeoCommand> Definitions = CreateDefinitions();

    public static readonly BeoCommand[] All =
    [
        // Source selection
        Get(CommandId.Tv),
        Get(CommandId.Radio),
        Get(CommandId.Cd),
        Get(CommandId.Cd2),
        Get(CommandId.Phono),
        Get(CommandId.Dvd),
        Get(CommandId.Sat),
        Get(CommandId.Vtape),
        Get(CommandId.Pc),
        Get(CommandId.Doorcam),
        Get(CommandId.Light),
        Get(CommandId.AAux),
        Get(CommandId.VAux),
        Get(CommandId.Atape),
        Get(CommandId.Atape2),
        Get(CommandId.Text),
        Get(CommandId.SpeakerDemo),

        // Numbers
        Get(CommandId.Digit0),
        Get(CommandId.Digit1),
        Get(CommandId.Digit2),
        Get(CommandId.Digit3),
        Get(CommandId.Digit4),
        Get(CommandId.Digit5),
        Get(CommandId.Digit6),
        Get(CommandId.Digit7),
        Get(CommandId.Digit8),
        Get(CommandId.Digit9),

        // Volume
        Get(CommandId.VolumeUp),
        Get(CommandId.VolumeDown),
        Get(CommandId.Mute),
        Get(CommandId.Loudness),

        // Power
        Get(CommandId.Standby),
        Get(CommandId.Off),
        Get(CommandId.AllStandby),
        Get(CommandId.AllOff),

        // Transport
        Get(CommandId.Go),
        Get(CommandId.Play),
        Get(CommandId.Stop),
        Get(CommandId.Record),

        // Navigation
        Get(CommandId.Up),
        Get(CommandId.Down),
        Get(CommandId.Left),
        Get(CommandId.Right),
        Get(CommandId.Menu),
        Get(CommandId.Exit),
        Get(CommandId.Return),
        Get(CommandId.Select),
        Get(CommandId.List),
        Get(CommandId.Index),

        // Sound
        Get(CommandId.Bass),
        Get(CommandId.Treble),
        Get(CommandId.Balance),
        Get(CommandId.Speaker),

        // Color keys
        Get(CommandId.Red),
        Get(CommandId.Green),
        Get(CommandId.Blue),
        Get(CommandId.Yellow),

        // Misc
        Get(CommandId.Store),
        Get(CommandId.Clear),
        Get(CommandId.Tune),
        Get(CommandId.Clock),
        Get(CommandId.Format),
        Get(CommandId.Picture),
        Get(CommandId.Turn),
        Get(CommandId.Av),
    ];
    public static readonly Dictionary<string, BeoCommand> ByCmd =
        All.ToDictionary(c => c.Cmd, StringComparer.OrdinalIgnoreCase);
    public static readonly HashSet<string> Names =
        new(All.Select(c => c.Cmd), StringComparer.OrdinalIgnoreCase);

    public static BeoCommand? Find(string cmd) =>
        ByCmd.TryGetValue(cmd, out var command) ? command : null;

    public static BeoCommand Get(CommandId id) => Definitions[id];

    public static BeoCommand? ResolveSourceCommand(string? statusText)
    {
        var statusKey = string.IsNullOrWhiteSpace(statusText)
            ? string.Empty
            : new string(statusText.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (string.IsNullOrEmpty(statusKey))
            return null;

        return All.FirstOrDefault(command =>
            command.Category == CommandCategory.Source &&
            statusKey.StartsWith(
                string.IsNullOrWhiteSpace(command.Cmd)
                    ? string.Empty
                    : new string(command.Cmd.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(),
                StringComparison.Ordinal));
    }

    /// <summary>Returns matching command names for a partial input.</summary>
    public static IEnumerable<string> Hints(string partial) =>
        Names.Where(n => n.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
             .Order();

    private static Dictionary<CommandId, BeoCommand> CreateDefinitions()
    {
        var definitions = new Dictionary<CommandId, BeoCommand>
        {
            [CommandId.Tv]          = new(CommandId.Tv,          "tv",         "TV",          CommandCategory.Source),
            [CommandId.Radio]       = new(CommandId.Radio,       "radio",      "RADIO",       CommandCategory.Source),
            [CommandId.Cd]          = new(CommandId.Cd,          "cd",         "CD",          CommandCategory.Source),
            [CommandId.Cd2]         = new(CommandId.Cd2,         "cd2",        "CD 2",        CommandCategory.Source),
            [CommandId.Phono]       = new(CommandId.Phono,       "phono",      "PHONO",       CommandCategory.Source),
            [CommandId.Dvd]         = new(CommandId.Dvd,         "dvd",        "DVD",         CommandCategory.Source),
            [CommandId.Sat]         = new(CommandId.Sat,         "sat",        "SAT",         CommandCategory.Source),
            [CommandId.Vtape]       = new(CommandId.Vtape,       "vtape",      "V.TAPE",      CommandCategory.Source),
            [CommandId.Pc]          = new(CommandId.Pc,          "pc",         "PC",          CommandCategory.Source),
            [CommandId.Doorcam]     = new(CommandId.Doorcam,     "doorcam",    "DOORCAM",     CommandCategory.Source),
            [CommandId.Light]       = new(CommandId.Light,       "light",      "LIGHT",       CommandCategory.Source),
            [CommandId.AAux]        = new(CommandId.AAux,        "a.aux",      "A.AUX",       CommandCategory.Source),
            [CommandId.VAux]        = new(CommandId.VAux,        "v.aux",      "V.AUX",       CommandCategory.Source),
            [CommandId.Atape]       = new(CommandId.Atape,       "atape",      "A.TAPE",      CommandCategory.Source),
            [CommandId.Atape2]      = new(CommandId.Atape2,      "atape2",     "A.TAPE 2",    CommandCategory.Source),
            [CommandId.Link]        = new(CommandId.Link,        "link",       "LINK",        CommandCategory.Source),
            [CommandId.Text]        = new(CommandId.Text,        "text",       "TEXT",        CommandCategory.Source),
            [CommandId.SpeakerDemo] = new(CommandId.SpeakerDemo, "spdemo",     "SP DEMO",     CommandCategory.Source),

            [CommandId.Digit0] = new(CommandId.Digit0, "0", "0", CommandCategory.Number),
            [CommandId.Digit1] = new(CommandId.Digit1, "1", "1", CommandCategory.Number),
            [CommandId.Digit2] = new(CommandId.Digit2, "2", "2", CommandCategory.Number),
            [CommandId.Digit3] = new(CommandId.Digit3, "3", "3", CommandCategory.Number),
            [CommandId.Digit4] = new(CommandId.Digit4, "4", "4", CommandCategory.Number),
            [CommandId.Digit5] = new(CommandId.Digit5, "5", "5", CommandCategory.Number),
            [CommandId.Digit6] = new(CommandId.Digit6, "6", "6", CommandCategory.Number),
            [CommandId.Digit7] = new(CommandId.Digit7, "7", "7", CommandCategory.Number),
            [CommandId.Digit8] = new(CommandId.Digit8, "8", "8", CommandCategory.Number),
            [CommandId.Digit9] = new(CommandId.Digit9, "9", "9", CommandCategory.Number),

            [CommandId.VolumeUp]   = new(CommandId.VolumeUp,   "vol+",       "VOL+",        CommandCategory.Volume),
            [CommandId.VolumeDown] = new(CommandId.VolumeDown, "vol-",       "VOL-",        CommandCategory.Volume),
            [CommandId.Mute]       = new(CommandId.Mute,       "mute",       "MUTE",        CommandCategory.Volume),
            [CommandId.Loudness]   = new(CommandId.Loudness,   "loudness",   "LOUDNESS",    CommandCategory.Volume),

            [CommandId.Standby]    = new(CommandId.Standby,    "standby",    "STANDBY",     CommandCategory.Power),
            [CommandId.Off]        = new(CommandId.Off,        "off",        "OFF",         CommandCategory.Power),
            [CommandId.AllStandby] = new(CommandId.AllStandby, "allstandby", "ALL STANDBY", CommandCategory.Power),
            [CommandId.AllOff]     = new(CommandId.AllOff,     "alloff",     "ALL OFF",     CommandCategory.Power),

            [CommandId.Go]     = new(CommandId.Go,     "go",     "GO",      CommandCategory.Transport),
            [CommandId.Goto]   = new(CommandId.Goto,   "goto",   "GO TO",   CommandCategory.Transport),
            [CommandId.Play]   = new(CommandId.Play,   "play",   "PLAY",    CommandCategory.Transport),
            [CommandId.Stop]   = new(CommandId.Stop,   "stop",   "STOP",    CommandCategory.Transport),
            [CommandId.Record] = new(CommandId.Record, "record", "RECORD",  CommandCategory.Transport),

            [CommandId.Up]     = new(CommandId.Up,     "up",     "UP",      CommandCategory.Navigation),
            [CommandId.Down]   = new(CommandId.Down,   "down",   "DOWN",    CommandCategory.Navigation),
            [CommandId.Left]   = new(CommandId.Left,   "left",   "LEFT",    CommandCategory.Navigation),
            [CommandId.Right]  = new(CommandId.Right,  "right",  "RIGHT",   CommandCategory.Navigation),
            [CommandId.Menu]   = new(CommandId.Menu,   "menu",   "MENU",    CommandCategory.Navigation),
            [CommandId.Exit]   = new(CommandId.Exit,   "exit",   "EXIT",    CommandCategory.Navigation),
            [CommandId.Return] = new(CommandId.Return, "return", "RETURN",  CommandCategory.Navigation),
            [CommandId.Select] = new(CommandId.Select, "select", "SELECT",  CommandCategory.Navigation),
            [CommandId.List]   = new(CommandId.List,   "list",   "LIST",    CommandCategory.Navigation),
            [CommandId.Index]  = new(CommandId.Index,  "index",  "INDEX",   CommandCategory.Navigation),

            [CommandId.Bass]    = new(CommandId.Bass,    "bass",    "BASS",    CommandCategory.Sound),
            [CommandId.Treble]  = new(CommandId.Treble,  "treble",  "TREBLE",  CommandCategory.Sound),
            [CommandId.Balance] = new(CommandId.Balance, "balance", "BALANCE", CommandCategory.Sound),
            [CommandId.Speaker] = new(CommandId.Speaker, "speaker", "SOUND",   CommandCategory.Sound),

            [CommandId.Red]    = new(CommandId.Red,    "red",    "RED",    CommandCategory.Color),
            [CommandId.Green]  = new(CommandId.Green,  "green",  "GREEN",  CommandCategory.Color),
            [CommandId.Blue]   = new(CommandId.Blue,   "blue",   "BLUE",   CommandCategory.Color),
            [CommandId.Yellow] = new(CommandId.Yellow, "yellow", "YELLOW", CommandCategory.Color),

            [CommandId.Store]   = new(CommandId.Store,   "store",   "STORE",   CommandCategory.Misc),
            [CommandId.Clear]   = new(CommandId.Clear,   "clear",   "CLEAR",   CommandCategory.Misc),
            [CommandId.Tune]    = new(CommandId.Tune,    "tune",    "TUNE",    CommandCategory.Misc),
            [CommandId.Clock]   = new(CommandId.Clock,   "clock",   "CLOCK",   CommandCategory.Misc),
            [CommandId.Format]  = new(CommandId.Format,  "format",  "FORMAT",  CommandCategory.Misc),
            [CommandId.Picture] = new(CommandId.Picture, "picture", "PICTURE", CommandCategory.Misc),
            [CommandId.Turn]    = new(CommandId.Turn,    "turn",    "TURN",    CommandCategory.Misc),
            [CommandId.Av]      = new(CommandId.Av,      "av",      "AV",      CommandCategory.Misc),

            [CommandId.Pc2Dvd2]        = new(CommandId.Pc2Dvd2,        "dvd2",       "DVD 2",      CommandCategory.Pc2Source),
            [CommandId.Pc2On]          = new(CommandId.Pc2On,          "on",         "ON",         CommandCategory.Pc2Source),
            [CommandId.Pc2BassUp]      = new(CommandId.Pc2BassUp,      "bass+",      "BASS+",      CommandCategory.Pc2Tone),
            [CommandId.Pc2BassDown]    = new(CommandId.Pc2BassDown,    "bass-",      "BASS-",      CommandCategory.Pc2Tone),
            [CommandId.Pc2TrebleUp]    = new(CommandId.Pc2TrebleUp,    "treble+",    "TREBLE+",    CommandCategory.Pc2Tone),
            [CommandId.Pc2TrebleDown]  = new(CommandId.Pc2TrebleDown,  "treble-",    "TREBLE-",    CommandCategory.Pc2Tone),
            [CommandId.Pc2BalanceUp]   = new(CommandId.Pc2BalanceUp,   "balance+",   "BALANCE+",   CommandCategory.Pc2Tone),
            [CommandId.Pc2BalanceDown] = new(CommandId.Pc2BalanceDown, "balance-",   "BALANCE-",   CommandCategory.Pc2Tone),

            [CommandId.AppHelp]     = new(CommandId.AppHelp,     "/help",      "/HELP",      CommandCategory.AppCommands),
            [CommandId.AppClear]    = new(CommandId.AppClear,    "/clear",     "/CLEAR",     CommandCategory.AppCommands),
            [CommandId.AppDebug]    = new(CommandId.AppDebug,    "/debug",     "/DEBUG",     CommandCategory.AppCommands),
            [CommandId.AppExit]     = new(CommandId.AppExit,     "/exit",      "/EXIT",      CommandCategory.AppCommands),
            [CommandId.AppPort]     = new(CommandId.AppPort,     "/port",      "/PORT",      CommandCategory.AppCommands),
            [CommandId.AppPortScan] = new(CommandId.AppPortScan, "/port scan", "/PORT SCAN", CommandCategory.AppCommands),
            [CommandId.AppBt]       = new(CommandId.AppBt,       "/bt",        "/BT",        CommandCategory.AppCommands),
            [CommandId.AppBtScan]   = new(CommandId.AppBtScan,   "/bt scan",   "/BT SCAN",   CommandCategory.AppCommands),
            [CommandId.AppBtLast]   = new(CommandId.AppBtLast,   "/bt-last",   "/BT-LAST",   CommandCategory.AppCommands),
            [CommandId.AppPc2]      = new(CommandId.AppPc2,      "/pc2",       "/PC2",       CommandCategory.AppCommands),
        };

        return definitions;
    }
}
