using BeoControlBlazorServices;

using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;

using System.ComponentModel;
using System.Text.Json;

namespace Spotify;

public sealed class SpotifyController : IDisposable
{
    private const int RestartTrackThresholdMs = 3000;
    private const string TokenFileName = "spotify-token.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Action<SpotifyNowPlaying?>? _nowPlayingCallback;
    private readonly CancellationTokenSource _pollCancellation = new();
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

    public static Task<SpotifyConnection> ConnectAsync(string clientId, string redirectUri)
    {
        return ConnectAsync(new SpotifyAppSettings
        {
            ClientId = clientId,
            RedirectUri = redirectUri
        });
    }

    public static async Task<SpotifyConnection> ConnectAsync(SpotifyAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var tokenPath = GetTokenPath();
        var token = await AuthenticateAsync(settings, tokenPath);

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            token = await AuthenticateAsync(settings, tokenPath, forceLogin: true);

        try
        {
            return await CreateConnectionAsync(settings.ClientId, tokenPath, token);
        }
        catch (APIException ex) when (RequiresNewLogin(ex))
        {
            Console.WriteLine("Spotify authorization is no longer valid. Starting a new login.");
            token = await AuthenticateAsync(settings, tokenPath, forceLogin: true);
            return await CreateConnectionAsync(settings.ClientId, tokenPath, token);
        }
    }

    private static async Task<SpotifyConnection> CreateConnectionAsync(
        string clientId,
        string tokenPath,
        PKCETokenResponse token)
    {
        var refreshToken = token.RefreshToken;
        var authenticator = new PKCEAuthenticator(clientId, token);
        authenticator.TokenRefreshed += (_, refreshedToken) =>
        {
            if (string.IsNullOrWhiteSpace(refreshedToken.RefreshToken))
                refreshedToken.RefreshToken = refreshToken;
            else
                refreshToken = refreshedToken.RefreshToken;

            SaveToken(tokenPath, refreshedToken);
        };

        var spotify = new SpotifyClient(
            SpotifyClientConfig.CreateDefault().WithAuthenticator(authenticator));
        var me = await spotify.UserProfile.Current();
        var devicesResponse = await spotify.Player.GetAvailableDevices();
        var devices = devicesResponse.Devices ?? [];

        return new SpotifyConnection(spotify, me.DisplayName ?? me.Id, devices);
    }

    private static bool RequiresNewLogin(APIException exception)
    {
        return exception is APIUnauthorizedException
            || string.Equals(exception.Message, "invalid_grant", StringComparison.OrdinalIgnoreCase);
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

                case SpotifyPlaybackCommand.PreviousPlaylist:
                    return await PlayRelativePlaylistAsync("previous");

                case SpotifyPlaybackCommand.NextPlaylist:
                    return await PlayRelativePlaylistAsync("next");


                //case SpotifyPlaybackCommand.PlaySongNr:
                //    try
                //    {

                //        var playback = await _spotify.Player.GetCurrentPlayback();
                //        var playlistId = playback.Context.Uri["spotify:playlist:".Length..];

                //        var items = await _spotify.Playlists.GetPlaylistItems(playlistId);

                //        await _spotify.Player.ResumePlayback(new PlayerResumePlaybackRequest
                //        {
                //            DeviceId = SelectedDevice.Id,
                //            ContextUri = $"spotify:playlist:{playlistId}",
                //            OffsetParam = new PlayerResumePlaybackRequest.Offset
                //            {
                //                Position = 4 // 5. Song
                //            }
                //        });

                //        return $"Playlists retrieved:  {playlistId}";
                //    }
                //    catch (Exception ex)
                //    {
                //        string s = ex.ToString();
                //        return s;
                //    }
                //    break;

                default:

                    throw new InvalidOperationException("Unknown command. Use: play, pause, next, prev, quit");
            }
        }
        catch (APIException ex)
        {
            return "Spotify API error.";
            //throw new InvalidOperationException($"Spotify API error: {ex.Message}", ex);
        }
    }
    public async Task<string> PlayRelativePlaylistAsync(string direction)
    {
        var playback = await _spotify.Player.GetCurrentPlayback();
        var currentPlaylistId = playback.Context.Uri["spotify:playlist:".Length..];

        var playlists = await _spotify.Playlists.CurrentUsers();
        var list = playlists.Items;

        var currentIndex = list.FindIndex(p => p.Id == currentPlaylistId);

        var nextIndex = direction == "next"
            ? (currentIndex + 1) % list.Count
            : (currentIndex - 1 + list.Count) % list.Count;

        var nextPlaylistId = list[nextIndex].Id;

        await _spotify.Player.ResumePlayback(new PlayerResumePlaybackRequest
        {
            DeviceId = SelectedDevice.Id,
            ContextUri = $"spotify:playlist:{nextPlaylistId}",
            OffsetParam = new PlayerResumePlaybackRequest.Offset
            {
                Position = 0 // Song 1
            }
        });

        return list[nextIndex].Name;
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

    public async Task ActivateSelectedDeviceAsync()
    {
        try
        {
            var playback = await _spotify.Player.GetCurrentPlayback();
            await _spotify.Player.TransferPlayback(new PlayerTransferPlaybackRequest([SelectedDevice.Id])
            {
                Play = playback?.IsPlaying
            });
        }
        catch (APIException ex)
        {
            //throw new InvalidOperationException($"Spotify API error: {ex.Message}", ex);
        }
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
        using var timer = new PeriodicTimer(SpotifyDefaults.NowPlayingPollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_pollCancellation.Token))
                await NotifyNowPlayingAsync();
        }
        catch (OperationCanceledException) when (_pollCancellation.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _pollCancellation.Cancel();
        _pollCancellation.Dispose();
    }

    private static string GetTokenPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appDataPath))
            throw new InvalidOperationException("Application data folder is unavailable for Spotify token storage.");

        var tokenDirectory = Path.Combine(appDataPath, "BeoControl");
        Directory.CreateDirectory(tokenDirectory);
        return Path.Combine(tokenDirectory, TokenFileName);
    }

    private static async Task<PKCETokenResponse> AuthenticateAsync(
        SpotifyAppSettings settings,
        string tokenPath,
        bool forceLogin = false)
    {
        if (!forceLogin && File.Exists(tokenPath))
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
                throw new InvalidOperationException($"Invalid JSON token cache in {tokenPath}: {ex.Message}", ex);
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

                SaveToken(tokenPath, token);
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
            Scope = SpotifyDefaults.Scopes
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

    private static void SaveToken(string tokenPath, PKCETokenResponse token)
    {
        File.WriteAllText(tokenPath, JsonSerializer.Serialize(token, JsonOptions));
    }
}
