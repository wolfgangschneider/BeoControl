using System.Diagnostics;
using System.Threading.Tasks;

namespace BeoControlBlazorServices;

public sealed class LaunchSpotifyService : ILaunchSpotifyService
{
    private const string SpotifyWebUrl = "https://open.spotify.com/";
    private const string SpotifyAppUrl = "spotify:";

    public Task OpenAsync(SpotifyLaunchMode launchMode)
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
