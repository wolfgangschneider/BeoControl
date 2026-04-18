using Microsoft.JSInterop;

using Spotify;

namespace BeoControlBlazorServices;

public sealed class BlazorSpotifyService : SpotifyServiceBase
{
    private const string SpotifyWebUrl = "https://open.spotify.com/";
    private const string SpotifyAppUrl = "spotify:";
    private const string SpotifyWebTarget = "spotifytab";
    private const string SpotifyWebFeatures = "popup=yes,width=1200,height=900";
    private readonly IJSRuntime _jsRuntime;

    public BlazorSpotifyService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public override Task OpenAsync(SpotifyLaunchMode launchMode)
    {
        if (launchMode == SpotifyLaunchMode.App)
            return _jsRuntime.InvokeVoidAsync("open", SpotifyAppUrl, "_self").AsTask();

        return _jsRuntime.InvokeVoidAsync("open", SpotifyWebUrl, SpotifyWebTarget, SpotifyWebFeatures).AsTask();
    }

}
