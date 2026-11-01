using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

/// <summary>US-134: anonymous seat map of a showing, run against a fixed clock.</summary>
public sealed class SeatMapTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StrictEnumConverterFactory() }
    };

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DateTime _now = new(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    private WebApplicationFactory<Program> _factory = null!;
    private int _auditorium;
    private int _otherAuditorium;
    private int[] _seats = [];

    public async Task InitializeAsync()
    {
        _connection.Open();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock(_now));
            }));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var a = new Auditorium { Name = "Salong 1", Rows = 2, SeatsPerRow = 2 };
        var other = new Auditorium { Name = "Salong 2", Rows = 1, SeatsPerRow = 1 };
        // Added out of order to prove the response is sorted by Row, then Number.
        var seats = new[]
        {
            new Seat { Auditorium = a, RowNumber = 2, SeatNumber = 2, Type = SeatType.VIP },
            new Seat { Auditorium = a, RowNumber = 1, SeatNumber = 2, Type = SeatType.Standard },
            new Seat { Auditorium = a, RowNumber = 2, SeatNumber = 1, Type = SeatType.VIP },
            new Seat { Auditorium = a, RowNumber = 1, SeatNumber = 1, Type = SeatType.Standard }
        };
        db.AddRange(a, other);
        db.AddRange(seats);
        db.Seats.Add(new Seat { Auditorium = other, RowNumber = 1, SeatNumber = 1, Type = SeatType.Standard });
        db.Movies.Add(new Movie { Title = "Dune", Genre = Genre.SciFi, AgeRating = AgeRating.Age11, LengthMinutes = 100 });
        db.Users.Add(new ApplicationUser { Id = "customer-1", UserName = "c@example.com", Email = "c@example.com", DisplayName = "Customer" });
        await db.SaveChangesAsync();
        (_auditorium, _otherAuditorium) = (a.Id, other.Id);
        _seats = seats.Select(s => s.Id).ToArray(); // [2/2, 1/2, 2/1, 1/1]
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> SeedShowing(DateTime start, ShowingStatus status = ShowingStatus.Scheduled, int? auditoriumId = null) =>
        WithDb(async db =>
        {
            var showing = new Showing
            {
                MovieId = await db.Movies.Select(m => m.Id).SingleAsync(), AuditoriumId = auditoriumId ?? _auditorium,
                StartTime = start, EndTime = start.AddMinutes(115), Status = status
            };
            db.Showings.Add(showing);
            await db.SaveChangesAsync();
            return showing.Id;
        });

    private Task<int> SeedBooking(int showingId, params int[] seatIds) =>
        WithDb(async db =>
        {
            var booking = new Booking
            {
                Reference = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), UserId = "customer-1", ShowingId = showingId,
                TotalPrice = 100, Status = BookingStatus.Confirmed, CreatedAt = _now,
                Seats = seatIds.Select(id => new BookingSeat { ShowingId = showingId, SeatId = id, Price = 50 }).ToList()
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            return booking.Id;
        });

    private Task CancelBooking(int bookingId) =>
        WithDb(async db =>
        {
            var booking = await db.Bookings.Include(b => b.Seats).SingleAsync(b => b.Id == bookingId);
            await new BookingService().CancelInternalAsync(booking);
            await db.SaveChangesAsync();
            return 0;
        });

    private async Task<SeatMapDto> GetMap(int showingId)
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/showings/{showingId}/seats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeatMapDto>(Json))!;
    }

    [Fact]
    public async Task US134_Ac1_Anonymous_request_returns_every_seat_ordered_with_type_and_configured_price()
    {
        var start = _now.AddDays(1);
        var id = await SeedShowing(start);

        var map = await GetMap(id);

        Assert.Equal(id, map.ShowingId);
        Assert.Equal(start, map.Start);
        Assert.Equal(DateTimeKind.Utc, map.Start.Kind);
        Assert.Equal(
            new[]
            {
                new SeatDto(_seats[3], 1, 1, SeatType.Standard, 140, false),
                new SeatDto(_seats[1], 1, 2, SeatType.Standard, 140, false),
                new SeatDto(_seats[2], 2, 1, SeatType.VIP, 200, false),
                new SeatDto(_seats[0], 2, 2, SeatType.VIP, 200, false)
            },
            map.Seats);
    }

    [Fact]
    public async Task US134_Ac1_The_map_holds_no_user_or_booking_data()
    {
        var id = await SeedShowing(_now.AddDays(1));
        await SeedBooking(id, _seats[0]);
        using var client = _factory.CreateClient();

        var body = await client.GetStringAsync($"/api/showings/{id}/seats");

        Assert.DoesNotContain("customer-1", body);
        Assert.DoesNotContain("reference", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task US134_Ac2_A_seat_in_a_confirmed_booking_is_taken_and_free_again_after_cancel()
    {
        var id = await SeedShowing(_now.AddDays(1));
        var bookingId = await SeedBooking(id, _seats[3], _seats[2]);

        var taken = await GetMap(id);
        await CancelBooking(bookingId);
        var freed = await GetMap(id);

        Assert.Equal(new[] { _seats[3], _seats[2] }.Order(), taken.Seats.Where(s => s.IsTaken).Select(s => s.SeatId).Order());
        Assert.Equal(2, taken.Seats.Count(s => s.IsTaken));
        Assert.DoesNotContain(freed.Seats, s => s.IsTaken);
    }

    [Fact]
    public async Task US134_Ac2_A_seat_booked_for_another_showing_stays_free()
    {
        var id = await SeedShowing(_now.AddDays(1));
        var other = await SeedShowing(_now.AddDays(2));
        await SeedBooking(other, _seats[0]);

        var map = await GetMap(id);
        var otherMap = await GetMap(other);

        Assert.DoesNotContain(map.Seats, s => s.IsTaken);
        Assert.Equal(new[] { _seats[0] }, otherMap.Seats.Where(s => s.IsTaken).Select(s => s.SeatId));
    }

    [Fact]
    public async Task US134_Ac2_Only_the_seats_of_the_showings_own_auditorium_are_listed()
    {
        var id = await SeedShowing(_now.AddDays(1), auditoriumId: _otherAuditorium);

        var map = await GetMap(id);

        Assert.Single(map.Seats);
        Assert.DoesNotContain(map.Seats, s => _seats.Contains(s.SeatId));
    }

    [Fact]
    public async Task US134_Ac3_Unknown_and_cancelled_showings_return_404_problem_details()
    {
        var cancelled = await SeedShowing(_now.AddDays(1), ShowingStatus.Cancelled);
        using var client = _factory.CreateClient();

        var unknown = await client.GetAsync("/api/showings/9999/seats");
        var gone = await client.GetAsync($"/api/showings/{cancelled}/seats");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("application/problem+json", gone.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task US134_Ac4_A_showing_that_has_started_is_viewable_but_not_bookable()
    {
        var started = await SeedShowing(_now.AddMinutes(-1));
        var startsNow = await SeedShowing(_now);
        var upcoming = await SeedShowing(_now.AddSeconds(1));

        var startedMap = await GetMap(started);

        Assert.False(startedMap.IsBookable);
        Assert.Equal(4, startedMap.Seats.Count);
        Assert.False((await GetMap(startsNow)).IsBookable);
        Assert.True((await GetMap(upcoming)).IsBookable);
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
