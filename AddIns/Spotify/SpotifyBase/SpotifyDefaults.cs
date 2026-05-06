namespace BeoControlBlazorServices;

public static class SpotifyDefaults
{
    public const string ClientId = "d241779ec817475db4bf6b5bd0a457c7";
    public const string AccountsAuthorizeUri = "https://accounts.spotify.com/authorize";
    public const string AccountsTokenUri = "https://accounts.spotify.com/api/token";
    public const string PlayerApiRootUri = "https://api.spotify.com/v1/me/player";
    public const string PlayerDevicesUri = $"{PlayerApiRootUri}/devices";
    public const string PlayerCurrentlyPlayingUri = $"{PlayerApiRootUri}/currently-playing";
    public const int MinNowPlayingPollIntervalSeconds = 1;
    public const int MaxNowPlayingPollIntervalSeconds = 7;
    public const int DefaultNowPlayingPollIntervalSeconds = 7;
    public static readonly string[] Scopes =
    [
        "user-read-private",
        "user-read-playback-state",
        "user-read-currently-playing",
        "user-modify-playback-state"
    ];

    public static TimeSpan NowPlayingPollInterval =>
        TimeSpan.FromSeconds(Math.Clamp(
            DefaultNowPlayingPollIntervalSeconds,
            MinNowPlayingPollIntervalSeconds,
            MaxNowPlayingPollIntervalSeconds));
}
