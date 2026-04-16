namespace BeoControlBlazorServices;

public enum SpotifyLaunchMode
{
    Web,
    App
}

public interface ILaunchSpotifyService
{
    Task OpenAsync(SpotifyLaunchMode launchMode);
}
