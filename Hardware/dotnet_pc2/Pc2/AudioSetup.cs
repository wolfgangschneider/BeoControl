namespace Beoported.Pc2;

public enum Pc2DefaultSource
{
    PC = 0,
    None = 1,
    ML = 2,
}

/// <summary>
/// Holds the audio parameters sent via the 0xE3 mixer command:
/// volume, bass, treble, balance, and loudness.
/// </summary>
public class AudioSetup
{
    /// <summary>Volume level 0–63. Default 40.</summary>
    public byte Volume  { get; set; } = 40;

    /// <summary>Bass adjustment, signed (-n to +n). Default 0 = flat.</summary>
    public sbyte Bass    { get; set; } = 0;

    /// <summary>Treble adjustment, signed (-n to +n). Default 0 = flat.</summary>
    public sbyte Treble  { get; set; } = 0;

    /// <summary>Balance, signed. 0 = center. Default 0.</summary>
    public sbyte Balance { get; set; } = 0;

    /// <summary>Loudness boost on/off. Default false.</summary>
    public bool  Loudness { get; set; } = false;

    /// <summary>Startup source behavior: local PC or no automatic source activation.</summary>
    public Pc2DefaultSource DefaultSource { get; set; } = Pc2DefaultSource.None;
}
