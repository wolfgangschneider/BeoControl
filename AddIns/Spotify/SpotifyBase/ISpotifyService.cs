namespace BeoControlBlazorServices;

public enum SpotifyLaunchMode
{
    Web,
    App
}

public interface ISpotifyService
{
    bool SupportsSpotifyConnectionState { get; }
    Task OpenAsync(SpotifyLaunchMode launchMode);
    Task<IReadOnlyList<string>> GetSpotifyDeviceNamesAsync();
    Task<string?> GetSpotifyConnectedDeviceNameAsync(string? preferredDeviceName);
    Task<bool> ExecuteSpotifyCommandAsync(string command, string? preferredDeviceName);
    Task<(string Song, string Interpret)?> GetSpotifyNowPlayingTextAsync(string? preferredDeviceName);
}
