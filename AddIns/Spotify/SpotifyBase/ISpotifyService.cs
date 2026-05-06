namespace BeoControlBlazorServices;

public sealed class SpotifyNowPlayingChangedEventArgs((string Song, string Interpret)? nowPlayingText) : EventArgs
{
    public (string Song, string Interpret)? NowPlayingText { get; } = nowPlayingText;
}

public enum SpotifyLaunchMode
{
    Web,
    App
}

public interface ISpotifyService
{
    event EventHandler<SpotifyNowPlayingChangedEventArgs>? NowPlayingChanged;
    Task OpenAsync(SpotifyLaunchMode launchMode);
    Task<IReadOnlyList<string>> GetSpotifyDeviceNamesAsync();
    Task<string?> GetSpotifyConnectedDeviceNameAsync(string? preferredDeviceName);
    Task<bool> ExecuteSpotifyCommandAsync(string command, string? preferredDeviceName);
    Task<(string Song, string Interpret)?> GetSpotifyNowPlayingTextAsync(string? preferredDeviceName);
}
