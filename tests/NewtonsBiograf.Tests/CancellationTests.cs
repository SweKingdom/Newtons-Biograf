using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

/// <summary>US-138: cancel a booking (customer 2-hour rule, staff override). The clock is fixed relative to the showing start.</summary>
public sealed class CancellationTests : IAsyncLifetime
{
    private const string Owner = "customer-a";
    private const string Other = "customer-b";
    private const string TooLate = "Bookings can only be cancelled up to 2 hours before the showing starts.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StrictEnumConverterFactory() }
    };

    private static readonly DateTime Start = new(DateTime.UtcNow.Year + 1, 3, 10, 18, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nb-cancel-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new() { Now = Start.AddHours(-3) };
    private readonly RecordingEvents _events = new();
    private WebApplicationFactory<Program> _factory = null!;
    private int _showing;
    private int _seat1;
    private int _seat2;
    private int _booking;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
                services.RemoveAll<IBookingEvents>();
                services.AddSingleton<IBookingEvents>(_events);
            }));
        _events.Factory = _factory;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var auditorium = new Auditorium { Name = "Salong 1", Rows = 1, SeatsPerRow = 2 };
        var seat1 = new Seat { Auditorium = auditorium, RowNumber = 1, SeatNumber = 1, Type = SeatType.Standard };
        var seat2 = new Seat { Auditorium = auditorium, RowNumber = 1, SeatNumber = 2, Type = SeatType.Standard };
        var movie = new Movie { Title = "Dune", Genre = Genre.SciFi, AgeRating = AgeRating.Age11, LengthMinutes = 100 };
        db.AddRange(auditorium, seat1, seat2, movie);
        foreach (var id in new[] { Owner, Other })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = $"{id}@example.com", Email = $"{id}@example.com", DisplayName = id });
        }
        await db.SaveChangesAsync();
        var showing = new Showing
        {
            MovieId = movie.Id, AuditoriumId = auditorium.Id, StartTime = Start, EndTime = Start.AddMinutes(115)
        };
        db.Showings.Add(showing);
        await db.SaveChangesAsync();
        var booking = new Booking
        {
            UserId = Owner, ShowingId = showing.Id, Reference = "CANCEL22", Status = BookingStatus.Confirmed,
            TotalPrice = 280, CreatedAt = Start.AddDays(-5),
            Seats =
            [
                new BookingSeat { SeatId = seat1.Id, ShowingId = showing.Id, Price = 140 },
                new BookingSeat { SeatId = seat2.Id, ShowingId = showing.Id, Price = 140 }
            ]
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        (_showing, _seat1, _seat2, _booking) = (showing.Id, seat1.Id, seat2.Id, booking.Id);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private HttpClient Client(string? userId, string role = Roles.Customer, params string[] permissions)
    {
        var client = _factory.CreateClient();
        if (userId is not null)
        {
            var token = _factory.Services.GetRequiredService<TokenService>().Create(
                new ApplicationUser { Id = userId, Email = $"{userId}@example.com", DisplayName = userId }, role, permissions).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private Task<HttpResponseMessage> Cancel(int id, string? userId, string role = Roles.Customer, params string[] permissions) =>
        Client(userId, role, permissions).PostAsync($"/api/bookings/{id}/cancel", null);

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<Booking> Saved() => WithDb(db => db.Bookings.Include(b => b.Seats).SingleAsync(b => b.Id == _booking));

    private async Task AssertUnchanged()
    {
        var saved = await Saved();
        Assert.Equal(BookingStatus.Confirmed, saved.Status);
        Assert.Null(saved.CancelledAt);
        Assert.All(saved.Seats, s => Assert.True(s.IsActive));
        Assert.Empty(_events.Raised);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(detail, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Ac1_A_customer_cancels_in_time_and_the_seats_can_be_booked_again()
    {
        var response = await Cancel(_booking, Owner);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var saved = await Saved();
        Assert.Equal(BookingStatus.Cancelled, saved.Status);
        Assert.Equal(_clock.Now, saved.CancelledAt);
        Assert.Equal(2, saved.Seats.Count);
        Assert.All(saved.Seats, s => Assert.False(s.IsActive));

        var map = await Client(Owner).GetFromJsonAsync<SeatMapDto>($"/api/showings/{_showing}/seats", Json);
        Assert.All(map!.Seats, s => Assert.False(s.IsTaken));
        var rebook = await Client(Other).PostAsJsonAsync("/api/bookings", new { ShowingId = _showing, SeatIds = new[] { _seat1, _seat2 } });
        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);
    }

    [Fact]
    public async Task Ac2_Cancelling_too_late_returns_409_and_changes_nothing()
    {
        _clock.Now = Start.AddHours(-1);

        var response = await Cancel(_booking, Owner);

        await AssertProblem(response, HttpStatusCode.Conflict, TooLate);
        await AssertUnchanged();
    }

    [Fact]
    public async Task Ac3_Exactly_two_hours_before_is_allowed()
    {
        _clock.Now = Start.AddHours(-2);

        Assert.Equal(HttpStatusCode.NoContent, (await Cancel(_booking, Owner)).StatusCode);
    }

    [Fact]
    public async Task Ac3_One_hour_59_minutes_before_is_too_late()
    {
        _clock.Now = Start.AddHours(-2).AddMinutes(1);

        await AssertProblem(await Cancel(_booking, Owner), HttpStatusCode.Conflict, TooLate);
        await AssertUnchanged();
    }

    [Fact]
    public async Task Ac4_Another_customers_booking_returns_403_and_stays_unchanged()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Cancel(_booking, Other)).StatusCode);
        await AssertUnchanged();
    }

    [Fact]
    public async Task Ac4_Another_customer_learns_nothing_about_the_state_of_a_late_booking()
    {
        _clock.Now = Start.AddHours(1);

        Assert.Equal(HttpStatusCode.Forbidden, (await Cancel(_booking, Other)).StatusCode);
    }

    [Fact]
    public async Task Ac4_An_unknown_booking_returns_404_and_no_token_returns_401()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Cancel(9999, Owner)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cancel(_booking, null)).StatusCode);
        await AssertUnchanged();
    }

    [Theory]
    [InlineData(Roles.Admin, new string[0])]
    [InlineData(Roles.Staff, new[] { Permissions.ManageBookings })]
    public async Task Ac5_Admin_and_staff_with_ManageBookings_cancel_at_any_time_including_after_the_start(string role, string[] permissions)
    {
        _clock.Now = Start.AddHours(1);

        var response = await Cancel(_booking, "staff-1", role, permissions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var saved = await Saved();
        Assert.Equal((BookingStatus.Cancelled, _clock.Now), (saved.Status, saved.CancelledAt));
        Assert.Single(_events.Raised);
    }

    [Fact]
    public async Task Ac5_Staff_without_ManageBookings_get_403()
    {
        var response = await Cancel(_booking, "staff-1", Roles.Staff, Permissions.ManageMovies);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertUnchanged();
    }

    [Theory]
    [InlineData(Roles.Customer)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Admin)]
    public async Task Ac6_Cancelling_a_cancelled_booking_returns_409_without_a_second_event_or_a_new_CancelledAt(string role)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await Cancel(_booking, Owner)).StatusCode);
        var first = (await Saved()).CancelledAt;
        _clock.Now = Start.AddHours(1);
        var caller = role == Roles.Customer ? Owner : "staff-1";

        var response = await Cancel(_booking, caller, role, Permissions.ManageBookings);

        await AssertProblem(response, HttpStatusCode.Conflict, "Booking is already cancelled.");
        Assert.Equal(first, (await Saved()).CancelledAt);
        Assert.Single(_events.Raised);
    }

    [Fact]
    public async Task Ac7_A_cancelled_booking_stays_in_the_history_with_the_same_reference_and_price()
    {
        await Cancel(_booking, Owner);

        var history = await Client(Owner).GetFromJsonAsync<List<BookingDto>>("/api/bookings/mine?scope=history", Json);

        var dto = Assert.Single(history!);
        Assert.Equal((_booking, "CANCEL22", 280m, BookingStatus.Cancelled), (dto.Id, dto.Reference, dto.TotalPrice, dto.Status));
        Assert.Empty((await Client(Owner).GetFromJsonAsync<List<BookingDto>>("/api/bookings/mine?scope=upcoming", Json))!);
    }

    [Fact]
    public async Task Ac8_BookingCancelled_is_raised_once_after_the_commit()
    {
        await Cancel(_booking, Owner);

        var e = Assert.Single(_events.Raised);
        Assert.Equal((_booking, "CANCEL22", _showing, Owner), (e.BookingId, e.Reference, e.ShowingId, e.UserId));
        Assert.Equal(BookingStatus.Cancelled, _events.StatusSeenByHandler);
    }

    [Fact]
    public async Task Ac8_No_event_is_raised_on_any_rejection()
    {
        await Cancel(_booking, Other);
        await Cancel(9999, Owner);
        await Cancel(_booking, null);
        _clock.Now = Start.AddHours(-1);
        await Cancel(_booking, Owner);

        Assert.Empty(_events.Raised);
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTime Now { get; set; }

        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
    }

    /// <summary>Records each event and reads the booking status from the database at the moment the handler runs.</summary>
    private sealed class RecordingEvents : IBookingEvents
    {
        public WebApplicationFactory<Program>? Factory { get; set; }
        public List<BookingCancelled> Raised { get; } = [];
        public BookingStatus? StatusSeenByHandler { get; private set; }

        public async Task BookingCancelledAsync(BookingCancelled e, CancellationToken ct = default)
        {
            Raised.Add(e);
            using var scope = Factory!.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            StatusSeenByHandler = await db.Bookings.Where(b => b.Id == e.BookingId).Select(b => b.Status).SingleAsync(ct);
        }
    }
}
