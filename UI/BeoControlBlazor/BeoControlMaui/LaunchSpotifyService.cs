namespace BeoControlBlazorServices;

public sealed class LaunchSpotifyService : ILaunchSpotifyService
{
    private const string SpotifyWebUri = "https://open.spotify.com/";
    private const string SpotifyAppUri = "spotify:";

    // maui kann im browser nicht tab recyclen
    public Task OpenAsync(SpotifyLaunchMode launchMode)
    {
        var uri = launchMode == SpotifyLaunchMode.App ? SpotifyAppUri : SpotifyWebUri;
        return Launcher.Default.OpenAsync(uri);
    }
}
