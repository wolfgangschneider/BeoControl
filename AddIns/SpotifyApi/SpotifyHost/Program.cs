using System.Text.Json;
using Spotify;
using SpotifyAPI.Web;

try
{
    var settings = await LoadSettingsAsync();
    var connection = await SpotifyController.ConnectAsync(settings);

    if (connection.Devices.Count == 0)
    {
        throw new InvalidOperationException(
            "No Spotify playback device is available. Open Spotify on a phone, desktop, or web player first.");
    }

    var selectedDevice = SelectDevice(connection.Devices, settings.PreferredDeviceName);
    var spotifyController = new SpotifyController(
        connection.Client,
        connection.CurrentUserDisplayName,
        selectedDevice,
        nowPlaying => WriteNowPlaying(nowPlaying));

    Console.WriteLine($"Signed in as {spotifyController.CurrentUserDisplayName}.");
    Console.WriteLine($"Using device: {spotifyController.SelectedDevice.Name}");
    Console.WriteLine();
    Console.WriteLine("Commands: play, pause, next, prev, quit");
    await spotifyController.RefreshNowPlayingAsync();

    while (true)
    {
        Console.Write("> ");
        var input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
        {
            continue;
        }

        var normalizedInput = input.Trim().ToLowerInvariant();
        if (normalizedInput is "quit" or "exit" or "q")
        {
            break;
        }

        try
        {
            var command = ParseCommand(input);
            var result = await spotifyController.ExecuteAsync(command);
            Console.WriteLine(result);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}

return;

static void WriteNowPlaying(SpotifyNowPlaying? nowPlaying)
{
    if (nowPlaying is null)
    {
        Console.WriteLine("Now playing: unavailable");
        return;
    }

    var state = nowPlaying.IsPlaying ? "playing" : "paused";
    Console.WriteLine($"Now playing: {nowPlaying.Title} - {nowPlaying.Artist} ({state})");
}

static Device SelectDevice(IReadOnlyList<Device> devices, string? preferredDeviceName)
{
    if (!string.IsNullOrWhiteSpace(preferredDeviceName))
    {
        return devices.FirstOrDefault(device =>
                   string.Equals(device.Name, preferredDeviceName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"Preferred device '{preferredDeviceName}' was not found.");
    }

    var defaultDevice = devices.FirstOrDefault(device => device.IsActive) ?? devices[0];

    Console.WriteLine("Available devices:");
    for (var index = 0; index < devices.Count; index++)
    {
        var marker = ReferenceEquals(devices[index], defaultDevice) ? "*" : " ";
        Console.WriteLine($"{marker} {index + 1}. {devices[index].Name} ({devices[index].Type})");
    }

    Console.Write("Choose device number or press Enter for default: ");
    var choice = Console.ReadLine()?.Trim();

    if (string.IsNullOrWhiteSpace(choice))
    {
        return defaultDevice;
    }

    return int.TryParse(choice, out var selectedIndex)
           && selectedIndex >= 1
           && selectedIndex <= devices.Count
        ? devices[selectedIndex - 1]
        : defaultDevice;
}

static SpotifyPlaybackCommand ParseCommand(string input)
{
    return input.Trim().ToLowerInvariant() switch
    {
        "play" or "start" or "resume" => SpotifyPlaybackCommand.Play,
        "pause" or "stop" => SpotifyPlaybackCommand.Pause,
        "next" or "n" => SpotifyPlaybackCommand.Next,
        "prev" or "previous" or "back" => SpotifyPlaybackCommand.Previous,
        _ => throw new InvalidOperationException("Unknown command. Use: play, pause, next, prev, quit")
    };
}

static async Task<SpotifyAppSettings> LoadSettingsAsync()
{
    const string settingsFileName = "spotifysettings.json";
    var settingsPath = Path.Combine(Environment.CurrentDirectory, settingsFileName);

    if (!File.Exists(settingsPath))
    {
        throw new InvalidOperationException(
            $"Create {settingsFileName} in this directory and set your Spotify app ClientId.");
    }

    try
    {
        await using var stream = File.OpenRead(settingsPath);
        var settings = await JsonSerializer.DeserializeAsync<SpotifyAppSettings>(stream)
            ?? throw new InvalidOperationException($"Could not read {settingsFileName}.");

        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new InvalidOperationException(
                $"Set ClientId in {settingsFileName} or via the SPOTIFY_CLIENT_ID environment variable.");
        }

        if (!Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out var redirectUri)
            || !string.Equals(redirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("RedirectUri must be a valid http:// URL.");
        }

        return settings with { RedirectUri = redirectUri.ToString() };
    }
    catch (JsonException ex)
    {
        throw new InvalidOperationException($"Invalid JSON configuration in {settingsFileName}: {ex.Message}", ex);
    }
}
