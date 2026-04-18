using BeoControlBlazorServices;

namespace Spotify;

public abstract class SpotifyServiceBase : ISpotifyService
{
    private const string SpotifyClientId = "d241779ec817475db4bf6b5bd0a457c7";
    private const string SpotifyRedirectUri = "http://127.0.0.1:5543/callback";

    public bool SupportsSpotifyConnectionState => false;

    public abstract Task OpenAsync(SpotifyLaunchMode launchMode);

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
