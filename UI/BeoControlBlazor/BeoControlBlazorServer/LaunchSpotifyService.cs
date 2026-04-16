using Microsoft.JSInterop;

namespace BeoControlBlazorServices;

public sealed class LaunchSpotifyService : ILaunchSpotifyService
{
    private const string SpotifyWebUrl = "https://open.spotify.com/";
    private const string SpotifyAppUrl = "spotify:";
    private const string SpotifyWebTarget = "spotifytab";
    private const string SpotifyWebFeatures = "popup=yes,width=1200,height=900";

    private readonly IJSRuntime _jsRuntime;

    public LaunchSpotifyService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public Task OpenAsync(SpotifyLaunchMode launchMode)
    {
        if (launchMode == SpotifyLaunchMode.App)
            return _jsRuntime.InvokeVoidAsync("open", SpotifyAppUrl, "_self").AsTask();

        return _jsRuntime.InvokeVoidAsync("open", SpotifyWebUrl, SpotifyWebTarget, SpotifyWebFeatures).AsTask();
    }
}
