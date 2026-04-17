using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

using Spotify;

namespace BeoControlBlazorServices;

public sealed class LaunchSpotifyService : ILaunchSpotifyService
{
    private const string SpotifyWebUri = "https://open.spotify.com/";
    private const string SpotifyAppUri = "spotify:";
    private const string SpotifyApiDevicesUri = "https://api.spotify.com/v1/me/player/devices";
    private const string SpotifyAuthorizeUri = "https://accounts.spotify.com/authorize";
    private const string SpotifyTokenUri = "https://accounts.spotify.com/api/token";
    private const string SpotifyClientId = "d241779ec817475db4bf6b5bd0a457c7";
    private const string SpotifyDesktopRedirectUri = "http://127.0.0.1:5543/callback";
    private const string SpotifyAndroidRedirectUri = "beocontrolspotify://callbac";
    private const string SpotifyTokenCacheFileName = "spotify-mobile-token.json";
    private static readonly string[] SpotifyScopes =
    [
        "user-read-private",
        "user-read-playback-state",
        "user-read-currently-playing",
        "user-modify-playback-state"
    ];

    // maui kann im browser nicht tab recyclen
    public Task OpenAsync(SpotifyLaunchMode launchMode)
    {
        var uri = launchMode == SpotifyLaunchMode.App ? SpotifyAppUri : SpotifyWebUri;
        return Launcher.Default.OpenAsync(uri);
    }

    public Task<IReadOnlyList<string>> GetSpotifyDeviceNamesAsync()
    {
        return DeviceInfo.Platform == DevicePlatform.Android
            ? GetAndroidSpotifyDeviceNamesAsync()
            : GetDesktopSpotifyDeviceNamesAsync();
    }

    private static async Task<IReadOnlyList<string>> GetDesktopSpotifyDeviceNamesAsync()
    {
        var connection = await SpotifyController.ConnectAsync(SpotifyClientId, SpotifyDesktopRedirectUri)
            ?? throw new InvalidOperationException("Spotify connection failed.");

        return connection.Devices
            .Select(device => device.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> GetAndroidSpotifyDeviceNamesAsync()
    {
        var token = await GetAndroidSpotifyTokenAsync();

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        using var response = await httpClient.GetAsync(SpotifyApiDevicesUri);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Spotify device request failed: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.GetProperty("devices")
            .EnumerateArray()
            .Select(device => device.TryGetProperty("name", out var name) ? name.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<SpotifyTokenCache> GetAndroidSpotifyTokenAsync()
    {
        var cachedToken = await LoadSpotifyTokenCacheAsync();
        if (cachedToken is not null && cachedToken.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            return cachedToken;

        if (!string.IsNullOrWhiteSpace(cachedToken?.RefreshToken))
        {
            try
            {
                return await RefreshSpotifyTokenAsync(cachedToken.RefreshToken);
            }
            catch
            {
            }
        }

        return await LoginForSpotifyTokenAsync();
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
            $"client_id={Uri.EscapeDataString(SpotifyClientId)}",
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
            new KeyValuePair<string, string>("client_id", SpotifyClientId),
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
            new KeyValuePair<string, string>("client_id", SpotifyClientId),
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
        return JsonSerializer.Deserialize<SpotifyTokenCache>(json);
    }

    private static Task SaveSpotifyTokenCacheAsync(SpotifyTokenCache token)
    {
        var tokenPath = Path.Combine(FileSystem.AppDataDirectory, SpotifyTokenCacheFileName);
        var json = JsonSerializer.Serialize(token);
        return File.WriteAllTextAsync(tokenPath, json);
    }

    private sealed record SpotifyTokenCache(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAtUtc);
}
