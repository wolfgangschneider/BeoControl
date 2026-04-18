using SpotifyAPI.Web;

namespace Spotify;

public sealed record SpotifyNowPlaying(string Title, string Artist, bool IsPlaying)
{
    public static SpotifyNowPlaying? FromPlayback(CurrentlyPlayingContext? playback)
    {
        if (playback?.Item is FullTrack track)
        {
            return new SpotifyNowPlaying(
                track.Name,
                string.Join(", ", track.Artists.Select(artist => artist.Name)),
                playback.IsPlaying);
        }

        if (playback?.Item is FullEpisode episode)
        {
            return new SpotifyNowPlaying(
                episode.Name,
                episode.Show?.Name ?? "Unknown show",
                playback.IsPlaying);
        }

        return null;
    }
}
