using BeoControl.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeoControlBlazor.Services;

public enum RemoteType { Beolink1000, Beo4 }

/// <summary>Persisted B&amp;O device settings (last transport type and device identifiers).</summary>
public class AppSettings
{
    private const string SettingsFile = "beocontrol.settings.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public DeviceType LastDevice { get; set; } = DeviceType.Serial;
    public RemoteType LastRemote { get; set; } = RemoteType.Beolink1000;

    /// <summary>Last connected serial port (port name + firmware name). Null = auto-detect.</summary>
    public DeviceInfo? LastSerial { get; set; }

    /// <summary>Last connected BLE device (MAC/device ID + name). Null = scan.</summary>
    public DeviceInfo? LastBluetooth { get; set; }

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
        try { File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, JsonOpts)); }
        catch { /* best-effort */ }
    }
}
