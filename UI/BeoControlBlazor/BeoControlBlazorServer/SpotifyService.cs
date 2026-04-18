using Microsoft.JSInterop;

using Spotify;

namespace BeoControlBlazorServices;

public sealed class SpotifyService : ISpotifyService
{
    private const string SpotifyWebUrl = "https://open.spotify.com/";
    private const string SpotifyAppUrl = "spotify:";
    private const string SpotifyWebTarget = "spotifytab";
    private const string SpotifyWebFeatures = "popup=yes,width=1200,height=900";
    private const string SpotifyClientId = "d241779ec817475db4bf6b5bd0a457c7";
    private const string SpotifyRedirectUri = "http://127.0.0.1:5543/callback";

    private readonly IJSRuntime _jsRuntime;

    public SpotifyService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public bool SupportsSpotifyConnectionState => false;

    public Task OpenAsync(SpotifyLaunchMode launchMode)
    {
        if (launchMode == SpotifyLaunchMode.App)
            return _jsRuntime.InvokeVoidAsync("open", SpotifyAppUrl, "_self").AsTask();

        return _jsRuntime.InvokeVoidAsync("open", SpotifyWebUrl, SpotifyWebTarget, SpotifyWebFeatures).AsTask();
    }

    public async Task<IReadOnlyList<string>> GetSpotifyDeviceNamesAsync()
    {
        var connection = await SpotifyController.ConnectAsync(SpotifyClientId, SpotifyRedirectUri)
            ?? throw new InvalidOperationException("Spotify connection failed.");

        return connection.Devices
            .Select(device => device.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();
    }

    public Task<string?> GetSpotifyConnectedDeviceNameAsync(string? preferredDeviceName) =>
        Task.FromResult<string?>(null);

    public Task<bool> ExecuteSpotifyCommandAsync(string command, string? preferredDeviceName) =>
        Task.FromResult(false);

    public Task<string?> GetSpotifyNowPlayingTextAsync(string? preferredDeviceName) =>
        Task.FromResult<string?>(null);
}
