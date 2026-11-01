using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace NewtonsBiograf.Client.Auth;

/// <summary>Attaches the Bearer token and reacts to 401 (back to login) and 403 (no-access page).</summary>
public class AuthMessageHandler(JwtAuthStateProvider auth, NavigationManager nav) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await auth.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        var isAuthRoute = request.RequestUri?.AbsolutePath.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase) == true;
        if (response.StatusCode == HttpStatusCode.Unauthorized && !isAuthRoute)
        {
            await auth.Logout();
            nav.NavigateTo(AuthRedirect.LoginUrl(nav));
        }
        else if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            nav.NavigateTo("no-access");
        }
        return response;
    }
}
