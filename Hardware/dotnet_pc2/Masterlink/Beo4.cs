namespace Beoported.Masterlink;

/// <summary>Beo4 IR remote keycodes.</summary>
public enum Beo4Key : byte
{
    Digit0     = 0x00,
    Digit1     = 0x01,
    Digit2     = 0x02,
    Digit3     = 0x03,
    Digit4     = 0x04,
    Digit5     = 0x05,
    Digit6     = 0x06,
    Digit7     = 0x07,
    Digit8     = 0x08,
    Digit9     = 0x09,
    Store      = 0x0B,
    Standby    = 0x0C,
    Mute       = 0x0D,
    ArrowUp    = 0x1E,
    ArrowDown  = 0x1F,
    ArrowLeft  = 0x32,
    ArrowRight = 0x34,
    Go         = 0x35,
    Play       = 0x35,
    Stop       = 0x36,
    Record     = 0x37,
    Sound      = 0x44,
    Picture    = 0x45,
    Menu       = 0x5C,
    VolUp      = 0x60,
    VolDown    = 0x64,
    Tv         = 0x80,
    Radio      = 0x81,
    VAux       = 0x82,
    AAux       = 0x83,
    VTape      = 0x85,
    Sat2       = 0x86,
    Text       = 0x88,
    Sat        = 0x8A,
    Pc         = 0x8B,
    ATape      = 0x91,
    Cd         = 0x92,
    Phono      = 0x93,
    ATape2     = 0x94,
    Av         = 0xBF,
    Yellow     = 0xD4,
    Green      = 0xD5,
    Blue       = 0xD8,
    Red        = 0xD9,
}

public static class Beo4
{
    private static readonly Dictionary<Beo4Key, Source> SourceMap = new()
    {
        [Beo4Key.Tv]    = Source.Tv,
        [Beo4Key.Radio] = Source.Radio,
        [Beo4Key.VAux]  = Source.VAux,
        [Beo4Key.VTape] = Source.VTape,
        [Beo4Key.Sat2]  = Source.Dvd,
        [Beo4Key.Sat]   = Source.Sat,
        [Beo4Key.Pc]    = Source.Pc,
        [Beo4Key.ATape] = Source.ATape,
        [Beo4Key.Cd]    = Source.Cd,
        [Beo4Key.Phono] = Source.Phono,
    };

    public static bool TryGetSource(Beo4Key key, out Source source) =>
        SourceMap.TryGetValue(key, out source);

    private static readonly HashSet<byte> SourceKeys = new()
    {
        0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x90, 0x91,
        0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0xA8, 0x47, 0xFA
    };

    public static bool IsSourceKey(byte code) => SourceKeys.Contains(code);
}
