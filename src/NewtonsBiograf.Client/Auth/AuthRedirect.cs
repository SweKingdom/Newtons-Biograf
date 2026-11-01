using Microsoft.AspNetCore.Components;

namespace NewtonsBiograf.Client.Auth;

public static class AuthRedirect
{
    /// <summary>The login address that brings the user back to the current page afterwards.</summary>
    public static string LoginUrl(NavigationManager nav) => LoginUrlFor(nav.ToBaseRelativePath(nav.Uri));

    /// <summary>The login address that brings the user back to <paramref name="path"/> afterwards.</summary>
    public static string LoginUrlFor(string path) => $"login?returnUrl={Uri.EscapeDataString(path)}";

    /// <summary>Where "Book" on a showing leads: the seat map when signed in, otherwise login that returns to it.</summary>
    public static string BookUrl(int showingId, bool signedIn)
    {
        var path = $"/showings/{showingId}/book";
        return signedIn ? path : LoginUrlFor(path);
    }

    /// <summary>Accepts only addresses inside this app, so a crafted returnUrl cannot send the user elsewhere.</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl)
        || returnUrl.StartsWith("//") || returnUrl.Contains('\\') || returnUrl.Contains(':')
            ? "/"
            : returnUrl;
}
