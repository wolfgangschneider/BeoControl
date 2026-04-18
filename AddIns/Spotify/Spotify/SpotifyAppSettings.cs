namespace Spotify;

public sealed record SpotifyAppSettings
{
    public string ClientId { get; init; } = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? string.Empty;
    public string RedirectUri { get; init; } = "http://127.0.0.1:5543/callback";
    public string? PreferredDeviceName { get; init; } = "Musikanlage";
}
