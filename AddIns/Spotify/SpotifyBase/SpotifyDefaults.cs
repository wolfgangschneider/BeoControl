namespace BeoControlBlazorServices;

public static class SpotifyDefaults
{
    public const string ClientId = "d241779ec817475db4bf6b5bd0a457c7";
    public const int MinNowPlayingPollIntervalSeconds = 1;
    public const int MaxNowPlayingPollIntervalSeconds = 7;
    public const int DefaultNowPlayingPollIntervalSeconds = 7;

    public static TimeSpan NowPlayingPollInterval =>
        TimeSpan.FromSeconds(Math.Clamp(
            DefaultNowPlayingPollIntervalSeconds,
            MinNowPlayingPollIntervalSeconds,
            MaxNowPlayingPollIntervalSeconds));
}
