using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;

namespace NewtonsBiograf.Tests;

/// <summary>Probe endpoints, one per policy. Registered only by the test factory below.</summary>
[ApiController]
[Route("api/probe")]
public class PolicyProbeController : ControllerBase
{
    [HttpGet("admin"), Authorize(Policy = Policies.AdminOnly)] public IActionResult Admin() => Ok();
    [HttpGet("customer"), Authorize(Policy = Policies.CustomerOnly)] public IActionResult Customer() => Ok();
    [HttpGet("movies"), Authorize(Policy = Policies.ManageMovies)] public IActionResult Movies() => Ok();
    [HttpGet("showings"), Authorize(Policy = Policies.ManageShowings)] public IActionResult Showings() => Ok();
    [HttpGet("bookings"), Authorize(Policy = Policies.ManageBookings)] public IActionResult Bookings() => Ok();
}

public sealed class AuthorizationTests : IDisposable
{
    private const string ProblemJson = "application/problem+json";

    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(PolicyProbeController).Assembly)));

    public void Dispose() => _factory.Dispose();

    private string TokenFor(string role, params string[] permissions) =>
        _factory.Services.GetRequiredService<TokenService>().Create(
            new ApplicationUser { Id = "user-1", Email = "u@example.com", DisplayName = "U" },
            role, permissions).Token;

    private async Task<HttpResponseMessage> Get(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _factory.CreateClient().SendAsync(request);
    }

    private static string CustomToken(string key, DateTime notBefore, DateTime expires) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "NewtonsBiograf", "NewtonsBiograf.Client", [new Claim("role", Roles.Admin)], notBefore, expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));

    [Theory]
    [InlineData(Policies.AdminOnly)]
    [InlineData(Policies.CustomerOnly)]
    [InlineData(Policies.ManageMovies)]
    [InlineData(Policies.ManageShowings)]
    [InlineData(Policies.ManageBookings)]
    public async Task Ac1_Policy_is_registered(string name)
    {
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.NotNull(await provider.GetPolicyAsync(name));
    }

    [Theory]
    // anonymous → 401 on every endpoint
    [InlineData("admin", "none", null, 401)]
    [InlineData("customer", "none", null, 401)]
    [InlineData("movies", "none", null, 401)]
    [InlineData("showings", "none", null, 401)]
    [InlineData("bookings", "none", null, 401)]
    // Customer: only the customer endpoint
    [InlineData("customer", "Customer", null, 200)]
    [InlineData("admin", "Customer", null, 403)]
    [InlineData("movies", "Customer", null, 403)]
    [InlineData("showings", "Customer", null, 403)]
    [InlineData("bookings", "Customer", null, 403)]
    // Staff without the claim → 403; with the matching claim → 200; with another claim → 403
    [InlineData("movies", "Staff", null, 403)]
    [InlineData("showings", "Staff", null, 403)]
    [InlineData("bookings", "Staff", null, 403)]
    [InlineData("movies", "Staff", Permissions.ManageMovies, 200)]
    [InlineData("showings", "Staff", Permissions.ManageShowings, 200)]
    [InlineData("bookings", "Staff", Permissions.ManageBookings, 200)]
    [InlineData("showings", "Staff", Permissions.ManageMovies, 403)]
    [InlineData("admin", "Staff", Permissions.ManageMovies, 403)]
    [InlineData("customer", "Staff", Permissions.ManageMovies, 403)]
    // Admin passes all permission policies without claims and AdminOnly, but is not a Customer
    [InlineData("admin", "Admin", null, 200)]
    [InlineData("movies", "Admin", null, 200)]
    [InlineData("showings", "Admin", null, 200)]
    [InlineData("bookings", "Admin", null, 200)]
    [InlineData("customer", "Admin", null, 403)]
    public async Task Ac2_Status_matrix(string endpoint, string role, string? permission, int expected)
    {
        var token = role == "none" ? null : TokenFor(role, permission is null ? [] : [permission]);

        var response = await Get($"/api/probe/{endpoint}", token);

        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Fact]
    public async Task Ac2_Expired_token_and_token_signed_with_another_key_return_401()
    {
        var now = DateTime.UtcNow;
        var expired = CustomToken(TestJwtKey.Value, now.AddHours(-2), now.AddHours(-1));
        var forged = CustomToken("another-signing-key-0123456789-abcdefghijkl", now.AddMinutes(-1), now.AddHours(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/api/probe/admin", expired)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/api/probe/admin", forged)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/probe/admin", CustomToken(TestJwtKey.Value, now.AddMinutes(-1), now.AddHours(1)))).StatusCode);
    }

    [Fact]
    public async Task Ac3_401_returns_problem_json_without_redirect()
    {
        var response = await Get("/api/probe/movies", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal(401, problem.Status);
        Assert.Equal("Authentication required", problem.Title);
    }

    [Fact]
    public async Task Ac3_403_returns_problem_json_without_redirect()
    {
        var response = await Get("/api/probe/movies", TokenFor(Roles.Customer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal(403, problem.Status);
        Assert.Equal("You do not have access to this resource", problem.Title);
    }
}
