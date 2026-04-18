using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Spotify;

namespace BeoControlBlazorServices;

public sealed class WebKitSpotifyService : SpotifyServiceBase
{
    private const string SpotifyWebUrl = "https://open.spotify.com/";
    private const string SpotifyAppUrl = "spotify:";
    public override Task OpenAsync(SpotifyLaunchMode launchMode)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = launchMode == SpotifyLaunchMode.App ? SpotifyAppUrl : SpotifyWebUrl,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

}
