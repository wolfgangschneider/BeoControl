using Beo4Adapter.Firmware;

using BeoControl.Interfaces;

using BeoControlBlazor.Services;

using Microsoft.Extensions.Logging;

using System.Reflection;

using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace BeoControlBlazorServices;

/// <summary>
/// Manual updates, shared by the desktop hosts — nothing installs by itself:
/// the hosts call <see cref="CheckForUpdateAsync"/> on startup, which only sets <see cref="AvailableVersion"/>;
/// the user installs any listed version (also downgrade) via <see cref="InstallVersionAsync"/>.
/// Versions come from the GitHub releases, prereleases included (everybody decides what to install).
/// (The Pages feed stays stable-only for installs that predate this service.)
/// The ESP32 firmware follows the app: the image bundled in Firmware/ has the app's version and is
/// pushed via OTA on request (<see cref="UpdateFirmwareAsync"/>), or installed over USB from the factory image (<see cref="InitFirmwareAsync"/>).
/// </summary>
public sealed class UpdateService(DeviceService deviceService, ILogger<UpdateService> logger)
{
    private const string FeedUrl = "https://wolfgangschneider.github.io/BeoControl/updates/";
    private const string RepoUrl = "https://github.com/wolfgangschneider/BeoControl";

    /// <summary>Raised when <see cref="AvailableVersion"/> or the firmware upload state changes.</summary>
    public event Action? Changed;

    /// <summary>Installed version, or null when not running from a Velopack install (dev build, Android).</summary>
    public string? CurrentVersion
    {
        get
        {
            var manager = new UpdateManager(FeedUrl);
            return manager.IsInstalled ? manager.CurrentVersion?.ToString() : null;
        }
    }

    /// <summary>Newer app version found by the last check, or null.</summary>
    public string? AvailableVersion { get; private set; }

    /// <summary>Looks for a newer release (no download); result in <see cref="AvailableVersion"/>.</summary>
    public async Task CheckForUpdateAsync()
    {
        try
        {
            var (_, manager) = CreateGithub(allowDowngrade: false);
            if (!manager.IsInstalled)
                return;
            var update = await manager.CheckForUpdatesAsync();
            AvailableVersion = update?.TargetFullRelease.Version.ToString();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Update check failed.");
        }
    }

    /// <summary>Versions on the GitHub releases for this install's channel (also prereleases), newest first. Empty when not installed.</summary>
    public async Task<List<string>> GetAvailableVersionsAsync()
    {
        var (source, manager) = CreateGithub();
        if (!manager.IsInstalled)
            return [];
        var assets = await GetFullAssetsAsync(source, manager);
        return assets.OrderByDescending(a => a.Version).Select(a => a.Version.ToString()).Distinct().ToList();
    }

    /// <summary>
    /// Downloads exactly <paramref name="version"/> (up- or downgrade), installs it and restarts the app —
    /// does not return in that case. Returns when already installed or not a Velopack install.
    /// </summary>
    public async Task InstallVersionAsync(string version)
    {
        var (source, manager) = CreateGithub();
        if (!manager.IsInstalled || manager.CurrentVersion?.ToString() == version)
            return;

        var target = (await GetFullAssetsAsync(source, manager)).FirstOrDefault(a => a.Version.ToString() == version)
            ?? throw new InvalidOperationException($"Version {version} not found in the GitHub releases.");

        logger.LogInformation("Installing version {Version}.", version);
        var update = new UpdateInfo(target, target.Version < manager.CurrentVersion!, null!, []);
        await manager.DownloadUpdatesAsync(update);
        manager.ApplyUpdatesAndRestart(target);
    }

    // ── ESP32 firmware ────────────────────────────────────────────────────────

    /// <summary>App version without build metadata (e.g. "1.4.0"); the bundled firmware carries the same version.</summary>
    public static string AppVersion =>
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
            .Split('+')[0];

    /// <summary>Bundled image for the connected device (Firmware/&lt;board&gt;.bin; older firmware without "board": device name), or null.</summary>
    public string? BundledFirmwarePath =>
        (deviceService.FirmwareBoard ?? deviceService.CurrentDevice?.Name) is { } name
        && Path.Combine(AppContext.BaseDirectory, "Firmware", name + ".bin") is var path
        && File.Exists(path) ? path : null;

    /// <summary>Connected via WiFi, version reported, differs from the bundled image, no upload running.</summary>
    public bool FirmwareUpdateAvailable =>
        deviceService.IsConnected && deviceService.CurrentDevice?.Type == DeviceType.WIFI
        && deviceService.FirmwareVersion is { } version && version != AppVersion
        && BundledFirmwarePath is not null && FirmwareProgress is null;

    /// <summary>Upload progress in percent, null when idle.</summary>
    public int? FirmwareProgress { get; private set; }

    /// <summary>
    /// Result of the last upload (e.g. "Firmware 1.4.0 installed"), null before the first one
    /// and while another device than the one it belongs to is connected.
    /// </summary>
    public string? FirmwareStatus =>
        deviceService.CurrentDevice is not { } device || device.Id == _firmwareStatusDevice ? _firmwareStatus : null;

    private string? _firmwareStatus;
    private string? _firmwareStatusDevice;

    private static readonly TimeSpan RebootWait = TimeSpan.FromSeconds(10);
    private const int ReconnectAttempts = 3;

    /// <summary>
    /// Pushes the bundled image via OTA, then reconnects: the device reboots and the old TCP
    /// connection would otherwise stay "connected" with the stale version.
    /// </summary>
    public async Task UpdateFirmwareAsync()
    {
        var path = BundledFirmwarePath;
        var host = deviceService.CurrentDevice?.Id;
        if (path is null || host is null || FirmwareProgress is not null)
            return;

        SetFirmware(0, $"Updating firmware to {AppVersion}…");
        try
        {
            var image = await File.ReadAllBytesAsync(path);
            await EspOta.UploadAsync(host, image, new Progress<int>(p => SetFirmware(p, FirmwareStatus)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Firmware update failed.");
            SetFirmware(null, $"Firmware update failed: {ex.Message}");
            return;
        }

        SetFirmware(null, $"Firmware {AppVersion} uploaded, device restarts…");
        deviceService.Disconnect();
        await Task.Delay(RebootWait);
        for (var attempt = 1; attempt <= ReconnectAttempts && !deviceService.IsConnected; attempt++)
        {
            await deviceService.ConnectWifiAsync(host);
            if (!deviceService.IsConnected)
                await Task.Delay(RebootWait / 2);
        }
        if (!deviceService.IsConnected)
        {
            SetFirmware(null, "Firmware uploaded, reconnect failed — connect via WIFI to check the version.");
            return;
        }

        // only the version the restarted device reports proves the update (it may have rolled back)
        for (var i = 0; i < 20 && deviceService.FirmwareVersion is null; i++)
            await Task.Delay(250);
        SetFirmware(null, deviceService.FirmwareVersion == AppVersion
            ? $"Firmware {AppVersion} installed."
            : $"Update not applied: device reports {deviceService.FirmwareVersion ?? "no version"} instead of {AppVersion}.");
    }

    /// <summary>Boards with a bundled factory image (Firmware/&lt;board&gt;-factory.bin), e.g. "Beo4Remote_AtomS3".</summary>
    public IReadOnlyList<string> FactoryImages =>
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Firmware"))
            ? Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Firmware"), "*-factory.bin")
                .Select(f => Path.GetFileName(f)[..^"-factory.bin".Length]).Order().ToList()
            : [];

    /// <summary>
    /// First install over USB: overwrites bootloader, partitions and app with the factory image of
    /// <paramref name="board"/>, whatever ran before; the image is contiguous from 0x0, so it also clears NVS (WiFi credentials, pins).
    /// Then optionally stores the device name ("name") and the IR pins ("txpin"), each restarts the device;
    /// without <paramref name="name"/> / <paramref name="txPin"/> the board defaults stay.
    /// </summary>
    public async Task InitFirmwareAsync(string port, string board, string? name, int? txPin, int? gndPin)
    {
        if (FirmwareProgress is not null)
            return;

        // the flasher needs the port for itself
        if (deviceService.IsConnected && deviceService.CurrentDevice?.Type == DeviceType.USB)
            deviceService.Disconnect();

        SetFirmware(0, $"Installing {board} {AppVersion} on {port}…");
        try
        {
            var image = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Firmware", board + "-factory.bin"));
            var chip = await EspFlasher.FlashAsync(port, image, 0, new Progress<int>(p => SetFirmware(p, FirmwareStatus)));
            if (!string.IsNullOrWhiteSpace(name))
            {
                SetFirmware(100, "Firmware written, setting name…");
                await SendAfterBootAsync(port, $"name {name.Trim()}");
            }
            if (txPin is { } tx)
            {
                SetFirmware(100, "Firmware written, setting IR pins…");
                await SendAfterBootAsync(port, gndPin is { } gnd ? $"txpin {tx} {gnd}" : $"txpin {tx}");
            }
            SetFirmware(null, $"{board} {AppVersion} installed on {chip}.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Firmware init failed.");
            SetFirmware(null, $"Init failed: {ex.Message}");
        }
    }

    /// <summary>Waits for the (re)booting device (USB-CDC re-enumerates) and sends <paramref name="command"/>.</summary>
    private static async Task SendAfterBootAsync(string port, string command)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(500);
            try
            {
                using var sp = new System.IO.Ports.SerialPort(port, 115200);
                sp.Open();
                await Task.Delay(3500);              // S3 boards wait up to 3 s for the host before reading commands
                sp.DiscardInBuffer();
                sp.WriteLine(command);
                // "... stored, restarting..." proves the device got it; else the port was still the one before the reset
                var reply = "";
                for (var i = 0; i < 20 && !reply.Contains("stored"); i++)
                {
                    await Task.Delay(100);
                    reply += sp.ReadExisting();
                }
                if (reply.Contains("stored"))
                    return;
            }
            catch (Exception) when (attempt < 19)
            {
                // port not back yet
            }
        }
        throw new TimeoutException($"Device did not confirm \"{command}\".");
    }

    private void SetFirmware(int? progress, string? status)
    {
        FirmwareProgress = progress;
        _firmwareStatus = status;
        _firmwareStatusDevice = deviceService.CurrentDevice?.Id;
        Changed?.Invoke();
    }

    private static (GithubSource Source, UpdateManager Manager) CreateGithub(bool allowDowngrade = true)
    {
        var source = new GithubSource(RepoUrl, null!, prerelease: true);
        return (source, new UpdateManager(source, new UpdateOptions { AllowVersionDowngrade = allowDowngrade }));
    }

    private static async Task<VelopackAsset[]> GetFullAssetsAsync(GithubSource source, UpdateManager manager)
    {
        // only releases that carry releases.<channel>.json + full .nupkg (published since the CI change) are listed
        var feed = await source.GetReleaseFeed(new NullVelopackLogger(), manager.AppId, VelopackLocator.Current.Channel, null, null!);
        return feed.Assets.Where(a => a.Type == VelopackAssetType.Full).ToArray();
    }
}
