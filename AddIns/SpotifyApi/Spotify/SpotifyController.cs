using System.ComponentModel;
using System.Text.Json;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;

namespace Spotify;

public sealed class SpotifyController
{
    private const int RestartTrackThresholdMs = 3000;
    private const string TokenFileName = "spotify-token.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Action<SpotifyNowPlaying?>? _nowPlayingCallback;
    private readonly SemaphoreSlim _nowPlayingLock = new(1, 1);
    private readonly SpotifyClient _spotify;
    private SpotifyNowPlaying? _lastNowPlaying;

    public SpotifyController(
        SpotifyClient spotify,
        string currentUserDisplayName,
        Device selectedDevice,
        Action<SpotifyNowPlaying?>? nowPlayingCallback = null)
    {
        _spotify = spotify ?? throw new ArgumentNullException(nameof(spotify));
        CurrentUserDisplayName = string.IsNullOrWhiteSpace(currentUserDisplayName)
            ? throw new ArgumentException("Current user display name is required.", nameof(currentUserDisplayName))
            : currentUserDisplayName;
        SelectedDevice = selectedDevice ?? throw new ArgumentNullException(nameof(selectedDevice));
        _nowPlayingCallback = nowPlayingCallback;

        if (_nowPlayingCallback is not null)
            _ = PollNowPlayingAsync();
    }

    public string CurrentUserDisplayName { get; }

    public Device SelectedDevice { get; }

    public static async Task<SpotifyConnection> ConnectAsync(SpotifyAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var tokenPath = Path.Combine(Environment.CurrentDirectory, TokenFileName);
        var token = await AuthenticateAsync(settings, tokenPath);

        var authenticator = new PKCEAuthenticator(settings.ClientId, token);
        authenticator.TokenRefreshed += (_, refreshedToken) =>
        {
            File.WriteAllText(tokenPath, JsonSerializer.Serialize(refreshedToken, JsonOptions));
        };

        var spotify = new SpotifyClient(SpotifyClientConfig.CreateDefault().WithAuthenticator(authenticator));

        try
        {
            var me = await spotify.UserProfile.Current();
            var devicesResponse = await spotify.Player.GetAvailableDevices();
            var devices = devicesResponse.Devices ?? [];
            return new SpotifyConnection(spotify, me.DisplayName ?? me.Id, devices);
        }
        catch (APIException ex)
        {
            throw new InvalidOperationException($"Spotify API error: {ex.Message}", ex);
        }
    }

    public async Task<string> ExecuteAsync(SpotifyPlaybackCommand command)
    {
        try
        {
            switch (command)
            {
                case SpotifyPlaybackCommand.Play:
                    await _spotify.Player.ResumePlayback(new PlayerResumePlaybackRequest
                    {
                        DeviceId = SelectedDevice.Id
                    });
                    await NotifyNowPlayingAsync(delayMs: 500);
                    return "Playback started.";

                case SpotifyPlaybackCommand.Pause:
                    await _spotify.Player.PausePlayback(new PlayerPausePlaybackRequest
                    {
                        DeviceId = SelectedDevice.Id
                    });
                    await NotifyNowPlayingAsync(delayMs: 500);
                    return "Playback paused.";

                case SpotifyPlaybackCommand.Next:
                    await _spotify.Player.SkipNext(new PlayerSkipNextRequest
                    {
                        DeviceId = SelectedDevice.Id
                    });
                    await NotifyNowPlayingAsync(delayMs: 500);
                    return "Skipped to next track.";

                case SpotifyPlaybackCommand.Previous:
                    return await RestartOrSkipPreviousAsync();

                default:
                    throw new InvalidOperationException("Unknown command. Use: play, pause, next, prev, quit");
            }
        }
        catch (APIException ex)
        {
            throw new InvalidOperationException($"Spotify API error: {ex.Message}", ex);
        }
    }

    private async Task<string> RestartOrSkipPreviousAsync()
    {
        var playback = await _spotify.Player.GetCurrentPlayback();

        if (playback?.ProgressMs > RestartTrackThresholdMs)
        {
            await _spotify.Player.SeekTo(new PlayerSeekToRequest(0)
            {
                DeviceId = SelectedDevice.Id
            });
            await NotifyNowPlayingAsync(delayMs: 500);

            return "Restarted current track.";
        }

        await _spotify.Player.SkipPrevious(new PlayerSkipPreviousRequest
        {
            DeviceId = SelectedDevice.Id
        });
        await NotifyNowPlayingAsync(delayMs: 500);

        return "Went to previous track.";
    }

    public async Task RefreshNowPlayingAsync()
    {
        await NotifyNowPlayingAsync();
    }

    private async Task NotifyNowPlayingAsync(int delayMs = 0)
    {
        if (_nowPlayingCallback is null)
        {
            return;
        }

        await _nowPlayingLock.WaitAsync();
        try
        {
            if (delayMs > 0)
                await Task.Delay(delayMs);

            var playback = await _spotify.Player.GetCurrentPlayback();
            var nowPlaying = SpotifyNowPlaying.FromPlayback(playback);
            if (EqualityComparer<SpotifyNowPlaying?>.Default.Equals(_lastNowPlaying, nowPlaying))
                return;

            _lastNowPlaying = nowPlaying;
            _nowPlayingCallback(nowPlaying);
        }
        finally
        {
            _nowPlayingLock.Release();
        }
    }

    private async Task PollNowPlayingAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync())
            await NotifyNowPlayingAsync();
    }

    private static async Task<PKCETokenResponse> AuthenticateAsync(SpotifyAppSettings settings, string tokenPath)
    {
        if (File.Exists(tokenPath))
        {
            try
            {
                await using var stream = File.OpenRead(tokenPath);
                var cachedToken = await JsonSerializer.DeserializeAsync<PKCETokenResponse>(stream);
                if (cachedToken is not null)
                {
                    return cachedToken;
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Invalid JSON token cache in {TokenFileName}: {ex.Message}", ex);
            }
        }

        var redirectUri = new Uri(settings.RedirectUri);
        var server = new EmbedIOAuthServer(redirectUri, redirectUri.Port);
        var completion = new TaskCompletionSource<PKCETokenResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (verifier, challenge) = PKCEUtil.GenerateCodes();

        server.AuthorizationCodeReceived += async (_, response) =>
        {
            try
            {
                await server.Stop();

                var token = await new OAuthClient().RequestToken(
                    new PKCETokenRequest(settings.ClientId, response.Code, redirectUri, verifier));

                File.WriteAllText(tokenPath, JsonSerializer.Serialize(token, JsonOptions));
                completion.TrySetResult(token);
            }
            catch (APIException ex)
            {
                completion.TrySetException(ex);
            }
            catch (IOException ex)
            {
                completion.TrySetException(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                completion.TrySetException(ex);
            }
        };

        await server.Start();

        var request = new LoginRequest(redirectUri, settings.ClientId, LoginRequest.ResponseType.Code)
        {
            CodeChallenge = challenge,
            CodeChallengeMethod = "S256",
            Scope =
            [
                "user-read-private",
                "user-read-playback-state",
                "user-read-currently-playing",
                "user-modify-playback-state"
            ]
        };

        var loginUri = request.ToUri();
        try
        {
            BrowserUtil.Open(loginUri);
        }
        catch (Win32Exception)
        {
            Console.WriteLine($"Open this URL in your browser to sign in:\n{loginUri}");
        }

        Console.WriteLine("Waiting for Spotify sign-in...");

        try
        {
            return await completion.Task;
        }
        catch (APIException ex)
        {
            throw new InvalidOperationException($"Spotify API error: {ex.Message}", ex);
        }
        finally
        {
            server.Dispose();
        }
    }
}
