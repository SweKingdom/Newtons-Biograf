using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Client.Auth;

/// <summary>Reads the claims out of a JWT payload. The signature is not checked here: the API enforces access.</summary>
public static class JwtParser
{
    public const string RoleClaimType = ClaimTypes.Role;
    public const string NameClaimType = "name";

    /// <summary>Returns the token's claims, or null when it is malformed, has no expiry or has expired.</summary>
    public static IReadOnlyList<Claim>? ParseClaims(string token, DateTimeOffset now)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var payload = document.RootElement;
            if (payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("exp", out var exp)
                || !exp.TryGetInt64(out var expiresAt)
                || DateTimeOffset.FromUnixTimeSeconds(expiresAt) < now)
            {
                return null;
            }

            var claims = new List<Claim>();
            foreach (var property in payload.EnumerateObject())
            {
                var type = property.Name == "role" ? RoleClaimType : property.Name;
                var values = property.Value.ValueKind == JsonValueKind.Array
                    ? property.Value.EnumerateArray().ToList()
                    : new List<JsonElement> { property.Value };
                claims.AddRange(values.Select(v =>
                    new Claim(type, v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText())));
            }
            return claims;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}

/// <summary>Keeps the API token in localStorage and exposes it to Blazor as the current user.</summary>
public class JwtAuthStateProvider(IJSRuntime js) : AuthenticationStateProvider
{
    public const string TokenKey = "nb_token";

    private static AuthenticationState Anonymous => new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await GetTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            return Anonymous;
        }

        var claims = JwtParser.ParseClaims(token, DateTimeOffset.UtcNow);
        if (claims is null)
        {
            await js.InvokeVoidAsync("localStorage.removeItem", TokenKey);
            return Anonymous;
        }

        var identity = new ClaimsIdentity(claims, "jwt", JwtParser.NameClaimType, JwtParser.RoleClaimType);
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public ValueTask<string?> GetTokenAsync() => js.InvokeAsync<string?>("localStorage.getItem", TokenKey);

    public async Task Login(LoginResponse response)
    {
        await js.InvokeVoidAsync("localStorage.setItem", TokenKey, response.Token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task Logout()
    {
        await js.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }
}
