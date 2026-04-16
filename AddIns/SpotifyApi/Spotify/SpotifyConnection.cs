using SpotifyAPI.Web;

namespace Spotify;

public sealed class SpotifyConnection
{
    public SpotifyConnection(SpotifyClient client, string currentUserDisplayName, IReadOnlyList<Device> devices)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        CurrentUserDisplayName = string.IsNullOrWhiteSpace(currentUserDisplayName)
            ? throw new ArgumentException("Current user display name is required.", nameof(currentUserDisplayName))
            : currentUserDisplayName;
        Devices = devices ?? throw new ArgumentNullException(nameof(devices));
    }

    public SpotifyClient Client { get; }

    public string CurrentUserDisplayName { get; }

    public IReadOnlyList<Device> Devices { get; }
}
