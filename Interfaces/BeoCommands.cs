namespace BeoControl.Interfaces;

public static class BeoCommands
{
    public static readonly CommandInfo[] All =
    [
        // Source selection
        new("tv",         "Select TV",                "Source",  "[n]"),
        new("radio",      "Select Radio",             "Source",  "[n]"),
        new("cd",         "Select CD",                "Source",  "[n]"),
        new("cd2",        "Select CD 2",              "Source"),
        new("phono",      "Select Phono",             "Source",  "[n]"),
        new("dvd",        "Select DVD",               "Source",  "[n]"),
        new("sat",        "Select SAT",               "Source",  "[n]"),
        new("vtape",      "Select V.Tape",            "Source",  "[n]"),
        new("pc",         "Select PC",                "Source",  "[n]"),
        new("doorcam",    "Doorcam",                  "Source"),
        new("light",      "Set Light source ctx",     "Source",  "[stop]"),
        new("a.aux",      "Audio Aux",                "Source"),
        new("v.aux",      "Video Aux",                "Source"),
        new("atape",      "Select A.Tape",            "Source",  "[n]"),
        new("atape2",     "Select A.Tape 2",          "Source",  "[n]"),
        new("text",       "Text",                     "Source"),
        new("spdemo",     "Speaker Demo",             "Source"),

        // Numbers
        new("0","Digit 0","Number"), new("1","Digit 1","Number"),
        new("2","Digit 2","Number"), new("3","Digit 3","Number"),
        new("4","Digit 4","Number"), new("5","Digit 5","Number"),
        new("6","Digit 6","Number"), new("7","Digit 7","Number"),
        new("8","Digit 8","Number"), new("9","Digit 9","Number"),

        // Volume
        new("vol+",       "Volume Up",                "Volume"),
        new("vol-",       "Volume Down",              "Volume"),
        new("mute",       "Mute",                     "Volume"),
        new("loudness",   "Loudness",                 "Volume"),

        // Power
        new("standby",    "Standby",                  "Power"),
        new("off",        "Standby (alias)",           "Power"),
        new("allstandby", "All devices standby",       "Power"),
        new("alloff",     "All devices standby (alias)","Power"),

        // Transport
        new("go",         "Play / Go",                "Transport"),
        new("play",       "Play (alias for go)",      "Transport"),
        new("stop",       "Stop",                     "Transport"),
        new("record",     "Record",                   "Transport"),

        // Navigation
        new("up",         "Cursor Up",                "Navigation"),
        new("down",       "Cursor Down",              "Navigation"),
        new("left",       "Cursor Left",              "Navigation"),
        new("right",      "Cursor Right",             "Navigation"),
        new("menu",       "Menu",                     "Navigation"),
        new("exit",       "Exit menu",                "Navigation"),
        new("return",     "Return",                   "Navigation"),
        new("select",     "Select",                   "Navigation"),
        new("list",       "List",                     "Navigation"),
        new("index",      "Index",                    "Navigation"),

        // Sound
        new("bass",       "Bass",                     "Sound"),
        new("treble",     "Treble",                   "Sound"),
        new("balance",    "Balance",                  "Sound"),
        new("speaker",    "Speaker",                  "Sound"),

        // Color keys
        new("red",        "Red key",                  "Color"),
        new("green",      "Green key",                "Color"),
        new("blue",       "Blue key",                 "Color"),
        new("yellow",     "Yellow key",               "Color"),

        // Misc
        new("store",      "Store",                    "Misc"),
        new("clear",      "Clear",                    "Misc"),
        new("tune",       "Tune",                     "Misc"),
        new("clock",      "Clock",                    "Misc"),
        new("format",     "Format",                   "Misc"),
        new("picture",    "Picture",                  "Misc"),
        new("turn",       "Turn / Mono",              "Misc"),
        new("av",         "AV",                       "Misc"),
    ];

    public static readonly HashSet<string> Names =
        new(All.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns matching command names for a partial input.</summary>
    public static IEnumerable<string> Hints(string partial) =>
        Names.Where(n => n.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
             .Order();
}
