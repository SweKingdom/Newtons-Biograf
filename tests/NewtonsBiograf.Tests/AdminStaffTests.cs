using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

public sealed class AdminStaffTests : IAsyncLifetime
{
    private const string Url = "/api/admin/staff";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplicationFactory<Program> _factory = null!;

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
        foreach (var role in new[] { Roles.Admin, Roles.Staff, Roles.Customer })
        {
            await roles.CreateAsync(new IdentityRole(role));
        }
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private string TokenFor(string role) =>
        _factory.Services.GetRequiredService<TokenService>().Create(
            new ApplicationUser { Id = "caller-1", Email = "caller@example.com", DisplayName = "Caller" }, role, []).Token;

    private async Task<HttpResponseMessage> Send(HttpMethod method, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, Url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        using var client = _factory.CreateClient();
        return await client.SendAsync(request);
    }

    private static object NewStaff(string name = "Sara", string email = "sara@example.com",
        string password = "password1", string[]? permissions = null) =>
        new { Name = name, Email = email, Password = password, Permissions = permissions ?? [] };

    private async Task<T> WithServices<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private Task<int> UserCount() => WithServices(sp => sp.GetRequiredService<AppDbContext>().Users.CountAsync());

    private Task SeedUser(string name, string email, string role, params string[] permissions) =>
        WithServices(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = name };
            await users.CreateAsync(user, "password1");
            await users.AddToRoleAsync(user, role);
            if (permissions.Length > 0)
            {
                await users.AddClaimsAsync(user, permissions.Select(p => new Claim(Permissions.ClaimType, p)));
            }
            return 0;
        });

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        return new Dictionary<string, string[]>(problem!.Errors, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ac1_Admin_creates_staff_with_role_Staff_and_one_claim_per_permission()
    {
        var response = await Send(HttpMethod.Post, TokenFor(Roles.Admin),
            NewStaff(permissions: [Permissions.ManageMovies, Permissions.ManageBookings, Permissions.ManageMovies]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(Url, response.Headers.Location?.OriginalString);
        var dto = (await response.Content.ReadFromJsonAsync<StaffDto>())!;
        Assert.Equal("Sara", dto.Name);
        Assert.Equal("sara@example.com", dto.Email);
        Assert.Equal([Permissions.ManageMovies, Permissions.ManageBookings], dto.Permissions);
        Assert.DoesNotContain("password1", await response.Content.ReadAsStringAsync());

        var (roles, claims, canLogIn) = await WithServices(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(dto.Id))!;
            return (await users.GetRolesAsync(user), await users.GetClaimsAsync(user),
                await users.CheckPasswordAsync(user, "password1"));
        });
        Assert.Equal([Roles.Staff], roles);
        Assert.Equal([Permissions.ManageMovies, Permissions.ManageBookings],
            claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).OrderByDescending(v => v));
        Assert.True(canLogIn);
    }

    [Fact]
    public async Task Ac1_Staff_can_be_created_with_no_permissions()
    {
        var response = await Send(HttpMethod.Post, TokenFor(Roles.Admin), new { Name = "Sara", Email = "sara@example.com", Password = "password1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<StaffDto>())!.Permissions);
        Assert.Empty(await WithServices(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            return await users.GetClaimsAsync((await users.FindByEmailAsync("sara@example.com"))!);
        }));
    }

    [Fact]
    public async Task Ac1_Fails_with_500_and_leaves_no_account_when_the_Staff_role_is_missing()
    {
        await WithServices(async sp =>
        {
            var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
            return await roles.DeleteAsync((await roles.FindByNameAsync(Roles.Staff))!);
        });

        var response = await Send(HttpMethod.Post, TokenFor(Roles.Admin), NewStaff(permissions: [Permissions.ManageMovies]));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await UserCount());
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData(Roles.Customer, 403)]
    [InlineData(Roles.Staff, 403)]
    [InlineData(Roles.Admin, 201)]
    public async Task Ac2_POST_status_by_caller(string? role, int expected)
    {
        var response = await Send(HttpMethod.Post, role is null ? null : TokenFor(role), NewStaff());

        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(expected == 201 ? 1 : 0, await UserCount());
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData(Roles.Customer, 403)]
    [InlineData(Roles.Staff, 403)]
    [InlineData(Roles.Admin, 200)]
    public async Task Ac2_GET_status_by_caller(string? role, int expected)
    {
        var response = await Send(HttpMethod.Get, role is null ? null : TokenFor(role));

        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Fact]
    public async Task Ac3_Unknown_permission_returns_400_naming_each_invalid_value_and_creates_nothing()
    {
        var response = await Send(HttpMethod.Post, TokenFor(Roles.Admin),
            NewStaff(permissions: [Permissions.ManageMovies, "Fly", "managemovies"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var messages = (await Errors(response))["Permissions"];
        Assert.Equal(2, messages.Length);
        Assert.Contains(messages, m => m.Contains("Fly"));
        Assert.Contains(messages, m => m.Contains("managemovies"));
        Assert.Equal(0, await UserCount());
    }

    [Fact]
    public async Task Ac3_Seven_character_password_returns_400_on_Password_and_creates_nothing()
    {
        var response = await Send(HttpMethod.Post, TokenFor(Roles.Admin), NewStaff(password: "short12"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))["Password"]);
        Assert.Equal(0, await UserCount());
    }

    [Fact]
    public async Task Ac3_Duplicate_email_returns_400_on_Email_and_creates_no_second_account()
    {
        await SeedUser("Existing", "sara@example.com", Roles.Customer);

        var response = await Send(HttpMethod.Post, TokenFor(Roles.Admin), NewStaff(email: "SARA@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))["Email"]);
        Assert.Equal(1, await UserCount());
    }

    [Fact]
    public async Task Ac3_Missing_name_and_invalid_email_return_per_field_errors()
    {
        var response = await Send(HttpMethod.Post, TokenFor(Roles.Admin), NewStaff(name: "", email: "not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await Errors(response);
        Assert.NotEmpty(errors["Name"]);
        Assert.NotEmpty(errors["Email"]);
        Assert.Equal(0, await UserCount());
    }

    [Fact]
    public async Task Ac4_List_returns_only_staff_ordered_by_name_with_their_permissions()
    {
        await SeedUser("Zed", "zed@example.com", Roles.Staff, Permissions.ManageShowings);
        await SeedUser("Anna", "anna@example.com", Roles.Staff);
        await SeedUser("Boss", "boss@example.com", Roles.Admin, Permissions.ManageMovies);
        await SeedUser("Cust", "cust@example.com", Roles.Customer);

        var response = await Send(HttpMethod.Get, TokenFor(Roles.Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<StaffDto[]>())!;
        Assert.Equal(["Anna", "Zed"], list.Select(s => s.Name));
        Assert.Empty(list[0].Permissions);
        Assert.Equal([Permissions.ManageShowings], list[1].Permissions);
        Assert.Equal("zed@example.com", list[1].Email);
    }

    [Fact]
    public async Task Ac4_List_is_empty_when_there_is_no_staff()
    {
        var response = await Send(HttpMethod.Get, TokenFor(Roles.Admin));

        Assert.Empty((await response.Content.ReadFromJsonAsync<StaffDto[]>())!);
    }
}
