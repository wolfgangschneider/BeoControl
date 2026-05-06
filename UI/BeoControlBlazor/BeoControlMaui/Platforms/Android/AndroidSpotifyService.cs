using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;

using Spotify;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;

namespace BeoControlBlazorServices;

public sealed class AndroidSpotifyService : SpotifyServiceBase
{
    private const string SpotifyWebUri = "https://open.spotify.com/";
    private const string SpotifyAppUri = "spotify:";
    private const string SpotifyAuthorizeUri = "https://accounts.spotify.com/authorize";
    private const string SpotifyTokenUri = "https://accounts.spotify.com/api/token";
    private const string SpotifyAndroidRedirectUri = "beocontrolspotify://callbac";
    private const string SpotifyTokenCacheFileName = "spotify-mobile-token.json";

    private static readonly SemaphoreSlim SpotifyTokenLock = new(1, 1);
    private static readonly string[] SpotifyScopes =
    [
        "user-read-private",
        "user-read-playback-state",
        "user-read-currently-playing",
        "user-modify-playback-state"
    ];

    private static SpotifyTokenCache? _cachedSpotifyToken;

    protected override string RedirectUri => SpotifyAndroidRedirectUri;

    public override Task OpenAsync(SpotifyLaunchMode launchMode)
    {
        var uri = launchMode == SpotifyLaunchMode.App ? SpotifyAppUri : SpotifyWebUri;
        return Launcher.Default.OpenAsync(uri);
    }

    protected override async Task<SpotifyConnection> ConnectAsync()
    {
        var token = await GetAndroidSpotifyTokenResponseAsync();
        var authenticator = new PKCEAuthenticator(SpotifyDefaults.ClientId, token);
        authenticator.TokenRefreshed += (_, refreshedToken) =>
        {
            _ = SaveSpotifyTokenCacheAsync(ToSpotifyTokenCache(refreshedToken, token.RefreshToken));
        };

        var spotify = new SpotifyClient(SpotifyClientConfig.CreateDefault().WithAuthenticator(authenticator));

        try
        {
            var me = await spotify.UserProfile.Current();
            var devicesResponse = await spotify.Player.GetAvailableDevices();
            var devices = devicesResponse.Devices ?? [];
            return new SpotifyConnection(spotify, me.DisplayName ?? me.Id, devices);
        }
        catch (APITooManyRequestsException ex)
        {
            throw new InvalidOperationException($"Rate limit hit. Waiting {ex.RetryAfter.TotalHours} hours...", ex);
        }
        catch (APIException ex)
        {
            throw new InvalidOperationException($"Spotify API error: {ex.Message}", ex);
        }
    }

    private static async Task<PKCETokenResponse> GetAndroidSpotifyTokenResponseAsync()
    {
        var token = await GetAndroidSpotifyTokenAsync();
        return ToPkceTokenResponse(token);
    }

    private static PKCETokenResponse ToPkceTokenResponse(SpotifyTokenCache token)
    {
        return new PKCETokenResponse
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken ?? string.Empty,
            TokenType = "Bearer",
            Scope = string.Join(" ", SpotifyScopes),
            ExpiresIn = Math.Max(1, (int)Math.Ceiling((token.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds)),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static SpotifyTokenCache ToSpotifyTokenCache(PKCETokenResponse token, string? fallbackRefreshToken = null)
    {
        return new SpotifyTokenCache(
            token.AccessToken,
            string.IsNullOrWhiteSpace(token.RefreshToken) ? fallbackRefreshToken : token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn));
    }

    private static async Task<SpotifyTokenCache> GetAndroidSpotifyTokenAsync()
    {
        await SpotifyTokenLock.WaitAsync();
        try
        {
            var cachedToken = _cachedSpotifyToken ?? await LoadSpotifyTokenCacheAsync();
            if (cachedToken is not null && cachedToken.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                _cachedSpotifyToken = cachedToken;
                return cachedToken;
            }

            if (!string.IsNullOrWhiteSpace(cachedToken?.RefreshToken))
            {
                try
                {
                    var refreshedToken = await RefreshSpotifyTokenAsync(cachedToken.RefreshToken);
                    _cachedSpotifyToken = refreshedToken;
                    return refreshedToken;
                }
                catch
                {
                }
            }

            var loginToken = await LoginForSpotifyTokenAsync();
            _cachedSpotifyToken = loginToken;
            return loginToken;
        }
        finally
        {
            SpotifyTokenLock.Release();
        }
    }

    private static async Task<SpotifyTokenCache> LoginForSpotifyTokenAsync()
    {
        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var callbackUri = new Uri(SpotifyAndroidRedirectUri);
        var authorizeUri = BuildAuthorizeUri(challenge);

        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(authorizeUri, callbackUri);
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("Spotify login was cancelled.");
        }

        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Spotify callback returned no authorization code.");

        return await ExchangeSpotifyCodeAsync(code, verifier);
    }

    private static Uri BuildAuthorizeUri(string challenge)
    {
        var query = string.Join("&",
        [
            $"client_id={Uri.EscapeDataString(SpotifyDefaults.ClientId)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(SpotifyAndroidRedirectUri)}",
            $"scope={Uri.EscapeDataString(string.Join(" ", SpotifyScopes))}",
            "code_challenge_method=S256",
            $"code_challenge={Uri.EscapeDataString(challenge)}"
        ]);

        return new Uri($"{SpotifyAuthorizeUri}?{query}");
    }

    private static async Task<SpotifyTokenCache> ExchangeSpotifyCodeAsync(string code, string verifier)
    {
        using var httpClient = new HttpClient();
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("client_id", SpotifyDefaults.ClientId),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", SpotifyAndroidRedirectUri),
            new KeyValuePair<string, string>("code_verifier", verifier)
        ]);

        return await SendSpotifyTokenRequestAsync(httpClient, content);
    }

    private static async Task<SpotifyTokenCache> RefreshSpotifyTokenAsync(string refreshToken)
    {
        using var httpClient = new HttpClient();
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("client_id", SpotifyDefaults.ClientId),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        ]);

        var refreshed = await SendSpotifyTokenRequestAsync(httpClient, content);
        if (string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            refreshed = refreshed with { RefreshToken = refreshToken };

        return refreshed;
    }

    private static async Task<SpotifyTokenCache> SendSpotifyTokenRequestAsync(HttpClient httpClient, FormUrlEncodedContent content)
    {
        using var response = await httpClient.PostAsync(SpotifyTokenUri, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Spotify token exchange failed: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        var accessToken = document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Spotify token response returned no access token.");
        var refreshToken = document.RootElement.TryGetProperty("refresh_token", out var refreshTokenElement)
            ? refreshTokenElement.GetString()
            : null;
        var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresInElement)
            ? expiresInElement.GetInt32()
            : 3600;

        var token = new SpotifyTokenCache(
            accessToken,
            refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn));

        await SaveSpotifyTokenCacheAsync(token);
        return token;
    }

    private static string CreateCodeVerifier()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static async Task<SpotifyTokenCache?> LoadSpotifyTokenCacheAsync()
    {
        var tokenPath = Path.Combine(FileSystem.AppDataDirectory, SpotifyTokenCacheFileName);
        if (!File.Exists(tokenPath))
            return null;

        var json = await File.ReadAllTextAsync(tokenPath);
        var token = JsonSerializer.Deserialize<SpotifyTokenCache>(json);
        _cachedSpotifyToken = token;
        return token;
    }

    private static Task SaveSpotifyTokenCacheAsync(SpotifyTokenCache token)
    {
        var tokenPath = Path.Combine(FileSystem.AppDataDirectory, SpotifyTokenCacheFileName);
        var json = JsonSerializer.Serialize(token);
        _cachedSpotifyToken = token;
        return File.WriteAllTextAsync(tokenPath, json);
    }

    private sealed record SpotifyTokenCache(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAtUtc);
}
