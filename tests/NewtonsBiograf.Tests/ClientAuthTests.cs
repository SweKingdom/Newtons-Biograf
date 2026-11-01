using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NewtonsBiograf.Client.Auth;

namespace NewtonsBiograf.Tests;

/// <summary>Client-side auth logic (US-123): token parsing, auth state, HTTP handler and API error mapping.</summary>
public class ClientAuthTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string TokenWith(Dictionary<string, object?> payload) =>
        $"{Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")}.{Base64Url(JsonSerializer.Serialize(payload))}.signature";

    /// <summary>Like the API: one permission is written as a plain string, several as an array.</summary>
    private static string ValidToken(string role = "Admin", params string[] permissions) => TokenWith(new()
    {
        ["sub"] = "user-1",
        ["name"] = "Åsa Ek",
        ["role"] = role,
        ["permission"] = permissions.Length == 1 ? permissions[0] : (object)permissions,
        ["exp"] = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()
    });

    private sealed class FakeLocalStorage : IJSRuntime
    {
        public Dictionary<string, string> Items { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            switch (identifier)
            {
                case "localStorage.setItem": Items[(string)args![0]!] = (string)args[1]!; break;
                case "localStorage.removeItem": Items.Remove((string)args![0]!); break;
                case "localStorage.getItem":
                    return new ValueTask<TValue>((TValue)(object?)Items.GetValueOrDefault((string)args![0]!)!);
            }
            return new ValueTask<TValue>(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation() => Initialize("http://localhost/", "http://localhost/movies");

        public string? Navigated { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options) => Navigated = uri;
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static LoginResponse LoginWith(string token) => new(token, DateTime.UtcNow.AddMinutes(30), "Åsa Ek", "Admin", []);

    private static async Task<(HttpResponseMessage Response, StubHandler Stub)> Send(
        JwtAuthStateProvider auth, FakeNavigation nav, HttpStatusCode status, string path)
    {
        var stub = new StubHandler(status);
        using var invoker = new HttpMessageInvoker(new AuthMessageHandler(auth, nav) { InnerHandler = stub });
        var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api.test" + path), default);
        return (response, stub);
    }

    [Fact]
    public void Ac1_ParseClaims_maps_name_role_and_every_permission()
    {
        var claims = JwtParser.ParseClaims(
            ValidToken("Staff", Permissions.ManageMovies, Permissions.ManageBookings), Now.AddYears(-1))!;
        var identity = new ClaimsIdentity(claims, "jwt", JwtParser.NameClaimType, JwtParser.RoleClaimType);

        Assert.Equal("Åsa Ek", identity.Name);
        Assert.True(new ClaimsPrincipal(identity).IsInRole(Roles.Staff));
        Assert.Equal([Permissions.ManageMovies, Permissions.ManageBookings],
            claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value));
    }

    [Fact]
    public void Ac1_ParseClaims_reads_a_single_permission_written_as_a_plain_string()
    {
        var claims = JwtParser.ParseClaims(ValidToken("Staff", Permissions.ManageShowings), Now.AddYears(-1))!;

        Assert.Equal(Permissions.ManageShowings, Assert.Single(claims, c => c.Type == Permissions.ClaimType).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b")]
    [InlineData("a.!!!.c")]
    [InlineData("a.bm90LWpzb24.c")] // payload "not-json"
    public void Ac1_ParseClaims_returns_null_for_a_malformed_token(string token)
    {
        Assert.Null(JwtParser.ParseClaims(token, Now));
    }

    [Fact]
    public void Ac1_ParseClaims_returns_null_for_an_expired_token_or_one_without_exp()
    {
        var expired = TokenWith(new() { ["name"] = "X", ["exp"] = Now.AddMinutes(-1).ToUnixTimeSeconds() });
        var noExp = TokenWith(new() { ["name"] = "X" });

        Assert.Null(JwtParser.ParseClaims(expired, Now));
        Assert.Null(JwtParser.ParseClaims(noExp, Now));
    }

    [Fact]
    public async Task Ac1_Login_stores_the_token_notifies_and_exposes_the_user()
    {
        var storage = new FakeLocalStorage();
        var auth = new JwtAuthStateProvider(storage);
        var notified = 0;
        auth.AuthenticationStateChanged += _ => notified++;

        Assert.False((await auth.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);

        var token = ValidToken();
        await auth.Login(LoginWith(token));
        var user = (await auth.GetAuthenticationStateAsync()).User;

        Assert.Equal(token, storage.Items[JwtAuthStateProvider.TokenKey]);
        Assert.Equal(1, notified);
        Assert.True(user.Identity!.IsAuthenticated);
        Assert.Equal("Åsa Ek", user.Identity.Name);
        Assert.True(user.IsInRole(Roles.Admin));
    }

    [Fact]
    public async Task Ac1_An_expired_stored_token_means_anonymous_and_is_removed()
    {
        var storage = new FakeLocalStorage();
        storage.Items[JwtAuthStateProvider.TokenKey] =
            TokenWith(new() { ["name"] = "X", ["exp"] = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds() });
        var auth = new JwtAuthStateProvider(storage);

        Assert.False((await auth.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Empty(storage.Items);
    }

    [Fact]
    public async Task Ac3_Logout_removes_the_token_and_the_user_is_anonymous()
    {
        var storage = new FakeLocalStorage();
        var auth = new JwtAuthStateProvider(storage);
        await auth.Login(LoginWith(ValidToken()));
        var notified = 0;
        auth.AuthenticationStateChanged += _ => notified++;

        await auth.Logout();

        Assert.Empty(storage.Items);
        Assert.Equal(1, notified);
        Assert.False((await auth.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task Ac1_Handler_attaches_the_Bearer_token_when_present_and_not_when_absent()
    {
        var storage = new FakeLocalStorage();
        var auth = new JwtAuthStateProvider(storage);
        var nav = new FakeNavigation();

        var (_, anonymous) = await Send(auth, nav, HttpStatusCode.OK, "/api/movies");
        var token = ValidToken();
        await auth.Login(LoginWith(token));
        var (_, signedIn) = await Send(auth, nav, HttpStatusCode.OK, "/api/movies");

        Assert.Null(anonymous.Seen!.Headers.Authorization);
        Assert.Equal("Bearer", signedIn.Seen!.Headers.Authorization!.Scheme);
        Assert.Equal(token, signedIn.Seen.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Ac3_Handler_logs_out_and_sends_the_user_to_login_on_401_from_a_protected_route()
    {
        var storage = new FakeLocalStorage();
        var auth = new JwtAuthStateProvider(storage);
        await auth.Login(LoginWith(ValidToken()));
        var nav = new FakeNavigation();

        await Send(auth, nav, HttpStatusCode.Unauthorized, "/api/admin/staff");

        Assert.Empty(storage.Items);
        Assert.Equal("login?returnUrl=movies", nav.Navigated);
    }

    [Fact]
    public async Task Ac1_Handler_leaves_a_401_from_the_login_route_to_the_form()
    {
        var storage = new FakeLocalStorage();
        var auth = new JwtAuthStateProvider(storage);
        var nav = new FakeNavigation();

        var (response, _) = await Send(auth, nav, HttpStatusCode.Unauthorized, "/api/auth/login");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(nav.Navigated);
    }

    [Fact]
    public async Task Ac3_Handler_shows_the_no_access_page_on_403_and_keeps_the_token()
    {
        var storage = new FakeLocalStorage();
        var auth = new JwtAuthStateProvider(storage);
        await auth.Login(LoginWith(ValidToken("Staff")));
        var nav = new FakeNavigation();

        await Send(auth, nav, HttpStatusCode.Forbidden, "/api/admin/staff");

        Assert.Equal("no-access", nav.Navigated);
        Assert.Single(storage.Items);
    }

    [Theory]
    [InlineData("/showings", "/showings")]
    [InlineData("movies/3?x=1", "movies/3?x=1")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    public void Ac1_SafeReturnUrl_only_allows_addresses_inside_the_app(string? returnUrl, string expected)
    {
        Assert.Equal(expected, AuthRedirect.SafeReturnUrl(returnUrl));
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string json, string contentType = "application/problem+json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, contentType) };

    [Fact]
    public async Task Ac1_ApiProblem_maps_validation_errors_to_fields_case_insensitively()
    {
        using var response = Problem(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"Password":["Password must be at least 8 characters."],"Email":["Enter a valid email address."]}}""");

        var problem = await ApiProblem.ReadAsync(response);

        Assert.Equal("Password must be at least 8 characters.", problem.Field("password"));
        Assert.Equal("Enter a valid email address.", problem.Field("EMAIL"));
        Assert.Null(problem.Field("Name"));
        Assert.Null(problem.General("Email", "Password"));
    }

    [Fact]
    public async Task Ac1_ApiProblem_shows_errors_not_tied_to_a_field_in_the_general_message()
    {
        using var response = Problem(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"":["Email is already taken."],"Name":["Enter your name."]}}""");

        var problem = await ApiProblem.ReadAsync(response);

        Assert.Equal("Email is already taken.", problem.General("Name"));
    }

    [Fact]
    public async Task Ac1_ApiProblem_uses_the_title_for_a_401_login_and_a_fallback_for_an_unreadable_body()
    {
        using var unauthorized = Problem(HttpStatusCode.Unauthorized, """{"title":"Invalid email or password.","status":401}""");
        using var html = Problem(HttpStatusCode.BadGateway, "<html></html>", "text/html");

        Assert.Equal("Invalid email or password.", (await ApiProblem.ReadAsync(unauthorized)).General("Email", "Password"));
        Assert.Equal("Something went wrong. Please try again.", (await ApiProblem.ReadAsync(html)).General());
    }
}
