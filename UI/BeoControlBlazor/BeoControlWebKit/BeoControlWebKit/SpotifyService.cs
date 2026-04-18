using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Spotify;

namespace BeoControlBlazorServices;

public sealed class SpotifyService : ISpotifyService
{
    private const string SpotifyWebUrl = "https://open.spotify.com/";
    private const string SpotifyAppUrl = "spotify:";
    private const string SpotifyClientId = "d241779ec817475db4bf6b5bd0a457c7";
    private const string SpotifyRedirectUri = "http://127.0.0.1:5543/callback";

    public bool SupportsSpotifyConnectionState => false;

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
