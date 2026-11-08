using Beo4Adapter.Transport;

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
/// App self-update via Velopack, shared by the desktop hosts.
/// PinnedVersion set → install and hold exactly that version from the GitHub releases (also downgrade);
/// otherwise UpdateMode.Auto → latest GitHub release (prereleases only with IncludePrereleases), UpdateMode.Off → nothing.
/// (The Pages feed stays stable-only for installs that predate this service.)
/// The ESP32 firmware follows the app: the image bundled in Firmware/ has the app's version and is
/// pushed via OTA when a WiFi-connected device reports a different one (automatically unless UpdateMode.Off).
/// </summary>
public sealed class UpdateService
{
    private const string FeedUrl = "https://wolfgangschneider.github.io/BeoControl/updates/";
    private const string RepoUrl = "https://github.com/wolfgangschneider/BeoControl";

    private readonly DeviceService deviceService;
    private readonly ILogger<UpdateService> logger;

    public UpdateService(DeviceService deviceService, ILogger<UpdateService> logger)
    {
        this.deviceService = deviceService;
        this.logger = logger;
        deviceService.OnStatusChanged += OnDeviceStatusChanged;
    }

    private AppSettings Settings => deviceService.Settings;

    /// <summary>Installed version, or null when not running from a Velopack install (dev build, Android).</summary>
    public string? CurrentVersion
    {
        get
        {
            var manager = new UpdateManager(FeedUrl);
            return manager.IsInstalled ? manager.CurrentVersion?.ToString() : null;
        }
    }

    /// <summary>Startup check. Returns true when an update was downloaded and scheduled — the caller must then quit the app.</summary>
    public async Task<bool> CheckOnStartupAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Settings.PinnedVersion))
                return await InstallVersionAsync(Settings.PinnedVersion);
            if (Settings.UpdateMode == UpdateMode.Off)
                return false;

            var (_, manager) = CreateGithub(Settings.IncludePrereleases, allowDowngrade: false);
            if (!manager.IsInstalled)
                return false;
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
                return false;

            logger.LogInformation("Update {Version} found, downloading.", update.TargetFullRelease.Version);
            await manager.DownloadUpdatesAsync(update);
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Update check failed.");
            return false;
        }
    }

    /// <summary>Versions on the GitHub releases for this install's channel (prereleases only with IncludePrereleases), newest first. Empty when not installed.</summary>
    public async Task<List<string>> GetAvailableVersionsAsync()
    {
        var (source, manager) = CreateGithub(Settings.IncludePrereleases);
        if (!manager.IsInstalled)
            return [];
        var assets = await GetFullAssetsAsync(source, manager);
        return assets.OrderByDescending(a => a.Version).Select(a => a.Version.ToString()).Distinct().ToList();
    }

    /// <summary>
    /// Downloads exactly <paramref name="version"/> (up- or downgrade) and schedules it for when the app exits.
    /// Returns true when scheduled, false when already installed or not a Velopack install.
    /// </summary>
    public async Task<bool> InstallVersionAsync(string version)
    {
        var (source, manager) = CreateGithub();
        if (!manager.IsInstalled || manager.CurrentVersion?.ToString() == version)
            return false;

        var target = (await GetFullAssetsAsync(source, manager)).FirstOrDefault(a => a.Version.ToString() == version)
            ?? throw new InvalidOperationException($"Version {version} not found in the GitHub releases.");

        logger.LogInformation("Installing pinned version {Version}.", version);
        var update = new UpdateInfo(target, target.Version < manager.CurrentVersion!, null!, []);
        await manager.DownloadUpdatesAsync(update);
        manager.WaitExitThenApplyUpdates(target);
        return true;
    }

    // ── ESP32 firmware ────────────────────────────────────────────────────────

    /// <summary>App version without build metadata (e.g. "1.4.0"); the bundled firmware carries the same version.</summary>
    public static string AppVersion =>
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
            .Split('+')[0];

    /// <summary>Bundled image for the connected device (Firmware/&lt;device name&gt;.bin), or null.</summary>
    public string? BundledFirmwarePath =>
        deviceService.CurrentDevice?.Name is { } name
        && Path.Combine(AppContext.BaseDirectory, "Firmware", name + ".bin") is var path
        && File.Exists(path) ? path : null;

    /// <summary>Connected via WiFi, version reported, differs from the bundled image, no upload running.</summary>
    public bool FirmwareUpdateAvailable =>
        deviceService.IsConnected && deviceService.CurrentDevice?.Type == DeviceType.WIFI
        && deviceService.FirmwareVersion is { } version && version != AppVersion
        && BundledFirmwarePath is not null && FirmwareProgress is null;

    /// <summary>Upload progress in percent, null when idle.</summary>
    public int? FirmwareProgress { get; private set; }

    /// <summary>Result of the last upload (e.g. "Firmware 1.4.0 installed"), null before the first one.</summary>
    public string? FirmwareStatus { get; private set; }

    public event Action? FirmwareChanged;

    /// <summary>Pushes the bundled image via OTA; the device reboots afterwards (connection drops).</summary>
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
            SetFirmware(null, $"Firmware {AppVersion} installed, device restarts.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Firmware update failed.");
            SetFirmware(null, $"Firmware update failed: {ex.Message}");
        }
    }

    private void SetFirmware(int? progress, string? status)
    {
        FirmwareProgress = progress;
        FirmwareStatus = status;
        FirmwareChanged?.Invoke();
    }

    private void OnDeviceStatusChanged(StatusMessage msg)
    {
        if (msg is FirmwareVersionMessage && Settings.UpdateMode == UpdateMode.Auto && FirmwareUpdateAvailable)
            _ = UpdateFirmwareAsync();
    }

    /// <param name="prerelease">Include prereleases. Default true so an already pinned prerelease stays installable.</param>
    private static (GithubSource Source, UpdateManager Manager) CreateGithub(bool prerelease = true, bool allowDowngrade = true)
    {
        var source = new GithubSource(RepoUrl, null!, prerelease);
        return (source, new UpdateManager(source, new UpdateOptions { AllowVersionDowngrade = allowDowngrade }));
    }

    private static async Task<VelopackAsset[]> GetFullAssetsAsync(GithubSource source, UpdateManager manager)
    {
        // only releases that carry releases.<channel>.json + full .nupkg (published since the CI change) are listed
        var feed = await source.GetReleaseFeed(new NullVelopackLogger(), manager.AppId, VelopackLocator.Current.Channel, null, null!);
        return feed.Assets.Where(a => a.Type == VelopackAssetType.Full).ToArray();
    }
}
