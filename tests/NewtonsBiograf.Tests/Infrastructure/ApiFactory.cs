using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Events;
using NewtonsBiograf.Api.Services;

namespace NewtonsBiograf.Tests.Infrastructure;

/// <summary>Records every BookingCancelled event the API raises.</summary>
public sealed class RecordingEvents : IBookingEvents
{
    public List<BookingCancelled> Raised { get; } = [];

    public Task BookingCancelledAsync(BookingCancelled e, CancellationToken ct = default)
    {
        lock (Raised)
        {
            Raised.Add(e);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// The real API running on the test SQLite database with the fake clock. It records BookingCreated and
/// BookingCancelled, and offers helpers for calling services directly and for signing in as a user.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly TestDb _db;

    public ApiFactory(TestDb db, FakeClock clock)
    {
        (_db, Clock) = (db, clock);
        Services.GetRequiredService<IBookingEventPublisher>().BookingCreated += (_, e) =>
        {
            lock (Created)
            {
                Created.Add(e);
            }
        };
    }

    public FakeClock Clock { get; }

    public RecordingEvents Cancelled { get; } = new();

    public List<BookingEventArgs> Created { get; } = [];

    /// <summary>Runs <paramref name="action"/> on a service from a fresh scope, so each call gets its own DbContext.</summary>
    public async Task<TResult> With<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>The identity a valid token would give the API: sub, role and one permission claim each.</summary>
    public static ClaimsPrincipal Principal(string userId, string role, params string[] permissions)
    {
        var claims = new List<Claim> { new("sub", userId), new(TokenService.RoleClaimType, role) };
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", "name", TokenService.RoleClaimType));
    }

    /// <summary>An HTTP client signed in with a JWT for the user, or anonymous when <paramref name="userId"/> is null.</summary>
    public HttpClient Client(string? userId, string role = Roles.Customer, params string[] permissions)
    {
        var client = CreateClient();
        if (userId is not null)
        {
            var token = Services.GetRequiredService<TokenService>().Create(
                new ApplicationUser { Id = userId, Email = $"{userId}@example.com", DisplayName = userId }, role, permissions).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => _db.Configure(options));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IBookingEvents>();
            services.AddSingleton<IBookingEvents>(Cancelled);
        });
}
