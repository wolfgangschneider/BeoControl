using BeoControlBlazorServices;

namespace Spotify;

public abstract class SpotifyServiceBase : ISpotifyService
{
    private string _preferredDeviceName = string.Empty;
    private (string Song, string Interpret)? _nowPlayingText;
    private Task<SpotifyController?>? _spotifyControllerTask;

    public bool SupportsSpotifyConnectionState => true;
    public bool SupportsSpotifyNowPlayingNotifications => true;
    public event EventHandler<SpotifyNowPlayingChangedEventArgs>? NowPlayingChanged;

    public abstract Task OpenAsync(SpotifyLaunchMode launchMode);

    protected virtual string RedirectUri => "http://127.0.0.1:5543/callback";

    protected virtual Task<SpotifyConnection> ConnectAsync()
    {
        return SpotifyController.ConnectAsync(SpotifyDefaults.ClientId, RedirectUri);
    }

    public async Task<IReadOnlyList<string>> GetSpotifyDeviceNamesAsync()
    {
        var connection = await ConnectAsync()
            ?? throw new InvalidOperationException("Spotify connection failed.");

        return connection.Devices
            .Select(device => device.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();
    }

    public async Task<string?> GetSpotifyConnectedDeviceNameAsync(string? preferredDeviceName)
    {
        var controller = await GetSpotifyControllerAsync(preferredDeviceName);
        return controller?.SelectedDevice.Name;
    }

    public async Task<bool> ExecuteSpotifyCommandAsync(string command, string? preferredDeviceName)
    {
        var spotifyCommand = command switch
        {
            "Play" => SpotifyPlaybackCommand.Play,
            "Pause" => SpotifyPlaybackCommand.Pause,
            "Next" => SpotifyPlaybackCommand.Next,
            "Previous" => SpotifyPlaybackCommand.Previous,
            _ => throw new InvalidOperationException($"Unsupported Spotify command '{command}'.")
        };

        var controller = await GetSpotifyControllerAsync(preferredDeviceName);
        if (controller is null)
            return false;

        await controller.ExecuteAsync(spotifyCommand);
        return true;
    }

    public async Task<(string Song, string Interpret)?> GetSpotifyNowPlayingTextAsync(string? preferredDeviceName)
    {
        var controller = await GetSpotifyControllerAsync(preferredDeviceName);
        if (controller is null)
        {
            SetNowPlayingText(null);
            return null;
        }

        if (_nowPlayingText is null)
            await controller.RefreshNowPlayingAsync();

        return _nowPlayingText;
    }

    private async Task<SpotifyController?> GetSpotifyControllerAsync(string? preferredDeviceName)
    {
        var normalizedDeviceName = preferredDeviceName?.Trim() ?? string.Empty;
        if (!string.Equals(_preferredDeviceName, normalizedDeviceName, StringComparison.Ordinal))
        {
            await DisposeSpotifyControllerAsync();
            _spotifyControllerTask = null;
            _preferredDeviceName = normalizedDeviceName;
            SetNowPlayingText(null);
        }

        return await (_spotifyControllerTask ??= ConnectSpotifyAsync(normalizedDeviceName));
    }

    private async Task DisposeSpotifyControllerAsync()
    {
        if (_spotifyControllerTask is null)
            return;

        var controller = await _spotifyControllerTask;
        controller?.Dispose();
    }

    private async Task<SpotifyController?> ConnectSpotifyAsync(string preferredDeviceName)
    {
        if (string.IsNullOrWhiteSpace(preferredDeviceName))
            return null;

        var connection = await ConnectAsync()
            ?? throw new InvalidOperationException("Spotify connection failed.");

        var selectedDevice = connection.Devices.FirstOrDefault(device =>
            string.Equals(device.Name, preferredDeviceName, StringComparison.OrdinalIgnoreCase));
        if (selectedDevice is null)
            return null;

        var controller = new SpotifyController(
            connection.Client,
            connection.CurrentUserDisplayName,
            selectedDevice,
            OnSpotifyNowPlayingChanged);
        await controller.ActivateSelectedDeviceAsync();
        await controller.RefreshNowPlayingAsync();
        return controller;
    }

    private void OnSpotifyNowPlayingChanged(SpotifyNowPlaying? nowPlaying)
    {
        SetNowPlayingText(nowPlaying?.IsPlaying == true
            ? (nowPlaying.Title ?? string.Empty, nowPlaying.Artist ?? string.Empty)
            : ("Spotify is paused", string.Empty));
    }

    private void SetNowPlayingText((string Song, string Interpret)? nowPlayingText)
    {
        if (_nowPlayingText == nowPlayingText)
            return;

        _nowPlayingText = nowPlayingText;
        NowPlayingChanged?.Invoke(this, new SpotifyNowPlayingChangedEventArgs(nowPlayingText));
    }
}
