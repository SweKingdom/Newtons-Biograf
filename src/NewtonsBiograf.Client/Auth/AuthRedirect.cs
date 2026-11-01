using Microsoft.AspNetCore.Components;

namespace NewtonsBiograf.Client.Auth;

public static class AuthRedirect
{
    /// <summary>The login address that brings the user back to the current page afterwards.</summary>
    public static string LoginUrl(NavigationManager nav) =>
        $"login?returnUrl={Uri.EscapeDataString(nav.ToBaseRelativePath(nav.Uri))}";

    /// <summary>Accepts only addresses inside this app, so a crafted returnUrl cannot send the user elsewhere.</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl)
        || returnUrl.StartsWith("//") || returnUrl.Contains('\\') || returnUrl.Contains(':')
            ? "/"
            : returnUrl;
}
