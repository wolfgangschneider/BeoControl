using BeoControl.Interfaces;
using Beoported.Pc2;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeoControlBlazor.Services;

public enum RemoteType { Beolink1000, Beo4 }

/// <summary>Persisted B&amp;O device settings (last transport type and device identifiers).</summary>
public class AppSettings
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BeoControl", "beocontrol.settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public DeviceType LastDevice { get; set; } = DeviceType.USB;
    public RemoteType LastRemote { get; set; } = RemoteType.Beolink1000;

    /// <summary>Last connected serial port (port name + firmware name). Null = auto-detect.</summary>
    public DeviceInfo? LastSerial { get; set; }

    /// <summary>Last connected BLE device (MAC/device ID + name). Null = scan.</summary>
    public DeviceInfo? LastBluetooth { get; set; }

    /// <summary>Last saved PC2 audio mixer settings.</summary>
    public AudioSetupDto AudioSetup { get; set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new AppSettings();
            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* best-effort */ }
    }

    /// <summary>Convert to <see cref="AudioSetup"/> for passing to <c>Pc2Device</c>.</summary>
    public AudioSetup ToAudioSetup() => new()
    {
        Volume   = AudioSetup.Volume,
        Bass     = AudioSetup.Bass,
        Treble   = AudioSetup.Treble,
        Balance  = AudioSetup.Balance,
        Loudness = AudioSetup.Loudness,
    };

    /// <summary>Update stored audio setup from a live <see cref="AudioSetup"/>.</summary>
    public void UpdateAudioSetup(AudioSetup src)
    {
        AudioSetup.Volume   = src.Volume;
        AudioSetup.Bass     = src.Bass;
        AudioSetup.Treble   = src.Treble;
        AudioSetup.Balance  = src.Balance;
        AudioSetup.Loudness = src.Loudness;
    }
}

/// <summary>Plain DTO for JSON serialization (avoids referencing Beoported.Pc2 in the JSON layer).</summary>
public class AudioSetupDto
{
    public byte  Volume   { get; set; } = 40;
    public sbyte Bass     { get; set; } = 0;
    public sbyte Treble   { get; set; } = 0;
    public sbyte Balance  { get; set; } = 0;
    public bool  Loudness { get; set; } = false;
}
