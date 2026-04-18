using Microsoft.Maui.ApplicationModel;

using Spotify;

namespace BeoControlBlazorServices;

public sealed class MauiSpotifyService : SpotifyServiceBase
{
    private const string SpotifyWebUri = "https://open.spotify.com/";
    private const string SpotifyAppUri = "spotify:";

    public override Task OpenAsync(SpotifyLaunchMode launchMode)
    {
        var uri = launchMode == SpotifyLaunchMode.App ? SpotifyAppUri : SpotifyWebUri;
        return Launcher.Default.OpenAsync(uri);
    }
}
