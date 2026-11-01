using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

/// <summary>Supplies the JWT signing key (a secret in real deployments) to every test host in this assembly.</summary>
internal static class TestJwtKey
{
    internal const string Value = "test-only-signing-key-0123456789-abcdefghij";

    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable("Jwt__Key", Value);
}

public sealed class AuthTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection.Open();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
            }));

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Staff", "Customer" })
        {
            await roles.CreateAsync(new IdentityRole(role));
        }

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private Task<HttpResponseMessage> Register(object body) => _client.PostAsJsonAsync("/api/auth/register", body);

    private Task<HttpResponseMessage> Login(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });

    private static object Anna(string email = "anna@example.com", string password = "password1") =>
        new { Name = "Anna", Email = email, Password = password };

    private async Task<T> WithServices<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private Task<int> UserCount() => WithServices(sp => sp.GetRequiredService<AppDbContext>().Users.CountAsync());

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        return new Dictionary<string, string[]>(problem!.Errors, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ac1_Register_returns_201_and_assigns_the_Customer_role()
    {
        var response = await Register(Anna());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.Equal("Anna", body?.Name);
        Assert.Equal("anna@example.com", body?.Email);
        var roles = await WithServices(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(body!.Id);
            Assert.Equal("Anna", user!.DisplayName);
            return await users.GetRolesAsync(user);
        });
        Assert.Equal(["Customer"], roles);
    }

    [Fact]
    public async Task Ac1_Register_fails_with_500_and_creates_no_user_when_the_Customer_role_is_missing()
    {
        await WithServices(async sp =>
        {
            var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
            return await roles.DeleteAsync((await roles.FindByNameAsync("Customer"))!);
        });

        var response = await Register(Anna());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await UserCount());
    }

    [Fact]
    public async Task Ac2_Password_shorter_than_8_characters_returns_400_and_creates_no_account()
    {
        var response = await Register(Anna(password: "short12"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))["Password"]);
        Assert.Equal(0, await UserCount());
    }

    [Fact]
    public async Task Ac2_Duplicate_email_returns_400_on_Email_and_creates_no_second_account()
    {
        await Register(Anna());

        var response = await Register(Anna(email: "ANNA@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))["Email"]);
        Assert.Equal(1, await UserCount());
    }

    [Fact]
    public async Task Ac2_Missing_name_and_invalid_email_return_per_field_errors()
    {
        var response = await Register(new { Name = "", Email = "not-an-email", Password = "password1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await Errors(response);
        Assert.NotEmpty(errors["Name"]);
        Assert.NotEmpty(errors["Email"]);
        Assert.False(errors.ContainsKey("Password"));
        Assert.Equal(0, await UserCount());
    }

    [Fact]
    public async Task Ac3_Login_returns_200_with_a_signed_JWT_carrying_the_claims()
    {
        var registered = await (await Register(Anna())).Content.ReadFromJsonAsync<RegisterResponse>();
        await WithServices(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(registered!.Id))!;
            return await users.AddClaimAsync(user, new Claim("permission", "ManageMovies"));
        });

        var response = await Login("Anna@Example.com", "password1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal("Anna", body.Name);
        Assert.Equal("Customer", body.Role);
        Assert.Equal(["ManageMovies"], body.Permissions);
        Assert.InRange(body.ExpiresAt, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));

        new JwtSecurityTokenHandler().ValidateToken(body.Token, new TokenValidationParameters
        {
            ValidIssuer = "NewtonsBiograf",
            ValidAudience = "NewtonsBiograf.Client",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey.Value))
        }, out _);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.Token);
        Assert.Equal(registered!.Id, jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("anna@example.com", jwt.Claims.Single(c => c.Type == "email").Value);
        Assert.Equal("Anna", jwt.Claims.Single(c => c.Type == "name").Value);
        Assert.Equal("Customer", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal("ManageMovies", jwt.Claims.Single(c => c.Type == "permission").Value);
    }

    [Fact]
    public async Task Ac3_Wrong_password_and_unknown_email_return_the_same_generic_401()
    {
        await Register(Anna());

        var wrongPassword = await Login("anna@example.com", "wrong-password");
        var unknownEmail = await Login("nobody@example.com", "password1");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var first = (await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>())!;
        var second = (await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("Invalid email or password.", first.Title);
        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.Status, second.Status);
    }

    [Fact]
    public async Task Ac4_Role_and_permission_fields_in_the_payload_are_ignored()
    {
        var response = await Register(new
        {
            Name = "Mallory",
            Email = "mallory@example.com",
            Password = "password1",
            Role = "Admin",
            Roles = new[] { "Admin" },
            Permissions = new[] { "ManageMovies" }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var (roles, claims) = await WithServices(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("mallory@example.com"))!;
            return (await users.GetRolesAsync(user), await users.GetClaimsAsync(user));
        });
        Assert.Equal(["Customer"], roles);
        Assert.Empty(claims);
    }

    [Fact]
    public void Startup_fails_when_the_JWT_key_is_shorter_than_32_characters()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "too-short" })));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Jwt:Key", error.ToString());
    }
}
