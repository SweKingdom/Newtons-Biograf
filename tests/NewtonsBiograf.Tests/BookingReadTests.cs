using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

/// <summary>US-136: my bookings (upcoming, history), booking detail and staff search.</summary>
public sealed class BookingReadTests : IAsyncLifetime
{
    private const string CustomerA = "customer-a";
    private const string CustomerB = "customer-b";
    private const string StaffId = "staff-1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StrictEnumConverterFactory() }
    };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nb-read-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private int _auditorium;
    private int _movie;
    private int[] _seats = [];

    // A: future (soon), future (later), cancelled future, confirmed past. B: shares the soon showing.
    private int _soon;
    private int _later;
    private int _past;
    private int _aSoon;
    private int _aLater;
    private int _aCancelled;
    private int _aPast;
    private int _bSoon;

    private static DateTime Now => DateTime.UtcNow;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
            }));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var auditorium = new Auditorium { Name = "Salong 1", Rows = 2, SeatsPerRow = 3 };
        var seats = new[]
        {
            new Seat { Auditorium = auditorium, RowNumber = 2, SeatNumber = 1, Type = SeatType.VIP },
            new Seat { Auditorium = auditorium, RowNumber = 1, SeatNumber = 2, Type = SeatType.Standard },
            new Seat { Auditorium = auditorium, RowNumber = 1, SeatNumber = 1, Type = SeatType.Standard },
            new Seat { Auditorium = auditorium, RowNumber = 1, SeatNumber = 3, Type = SeatType.Standard },
            new Seat { Auditorium = auditorium, RowNumber = 2, SeatNumber = 2, Type = SeatType.Standard }
        };
        var movie = new Movie { Title = "Dune", Genre = Genre.SciFi, AgeRating = AgeRating.Age11, LengthMinutes = 100 };
        db.AddRange(auditorium, movie);
        db.AddRange(seats);
        foreach (var id in new[] { CustomerA, CustomerB, StaffId })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = $"{id}@example.com", Email = $"{id}@example.com", DisplayName = id });
        }
        await db.SaveChangesAsync();
        (_auditorium, _movie, _seats) = (auditorium.Id, movie.Id, seats.Select(s => s.Id).ToArray());

        _soon = await AddShowing(db, Now.AddDays(2));
        _later = await AddShowing(db, Now.AddDays(5));
        _past = await AddShowing(db, Now.AddDays(-3));
        _aSoon = await AddBooking(db, CustomerA, _soon, "AAAA2222", BookingStatus.Confirmed, _seats[0], _seats[1]);
        _aLater = await AddBooking(db, CustomerA, _later, "AAAA3333", BookingStatus.Confirmed, _seats[0]);
        _aCancelled = await AddBooking(db, CustomerA, _later, "AAAA4444", BookingStatus.Cancelled, _seats[1]);
        _aPast = await AddBooking(db, CustomerA, _past, "AAAA5555", BookingStatus.Confirmed, _seats[0]);
        _bSoon = await AddBooking(db, CustomerB, _soon, "BBBB2222", BookingStatus.Confirmed, _seats[2]);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private async Task<int> AddShowing(AppDbContext db, DateTime start)
    {
        var showing = new Showing
        {
            MovieId = _movie, AuditoriumId = _auditorium, StartTime = start, EndTime = start.AddMinutes(115)
        };
        db.Showings.Add(showing);
        await db.SaveChangesAsync();
        return showing.Id;
    }

    private static async Task<int> AddBooking(AppDbContext db, string userId, int showingId, string reference,
        BookingStatus status, params int[] seatIds)
    {
        var cancelled = status == BookingStatus.Cancelled;
        var booking = new Booking
        {
            UserId = userId, ShowingId = showingId, Reference = reference, Status = status, TotalPrice = 140 * seatIds.Length,
            CreatedAt = DateTime.UtcNow.AddDays(-10), CancelledAt = cancelled ? DateTime.UtcNow.AddDays(-1) : null,
            Seats = seatIds.Select(id => new BookingSeat { SeatId = id, ShowingId = showingId, Price = 140, IsActive = !cancelled }).ToList()
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
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

    private async Task<List<BookingDto>> Mine(string scope, string userId = CustomerA)
    {
        var response = await Client(userId).GetAsync($"/api/bookings/mine?scope={scope}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<BookingDto>>(Json))!;
    }

    [Fact]
    public async Task Ac1_Upcoming_holds_only_the_callers_confirmed_bookings_of_showings_that_have_not_started_earliest_first()
    {
        var upcoming = await Mine("upcoming");

        Assert.Equal(new[] { _aSoon, _aLater }, upcoming.Select(b => b.Id));
        Assert.All(upcoming, b => Assert.Equal(BookingStatus.Confirmed, b.Status));
    }

    [Fact]
    public async Task Ac2_History_holds_cancelled_and_started_bookings_with_status_latest_first()
    {
        var history = await Mine("history");

        Assert.Equal(new[] { _aCancelled, _aPast }, history.Select(b => b.Id));
        Assert.Equal(new[] { BookingStatus.Cancelled, BookingStatus.Confirmed }, history.Select(b => b.Status));
        Assert.NotNull(history[0].CancelledAt);
        Assert.Equal(DateTimeKind.Utc, history[0].CancelledAt!.Value.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?scope=")]
    [InlineData("?scope=all")]
    [InlineData("?scope=1")]
    public async Task Ac3_A_missing_or_unknown_scope_returns_400(string query)
    {
        var response = await Client(CustomerA).GetAsync($"/api/bookings/mine{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ac3_Scope_is_case_insensitive_and_upcoming_plus_history_hold_each_booking_once()
    {
        var upcoming = await Mine("UPCOMING");
        var history = await Mine("History");

        var all = upcoming.Concat(history).Select(b => b.Id).Order().ToArray();
        Assert.Equal(new[] { _aSoon, _aLater, _aCancelled, _aPast }.Order(), all);
    }

    [Fact]
    public async Task Ac4_The_owner_gets_the_booking_dto_with_utc_times_and_seats_in_row_order()
    {
        var response = await Client(CustomerA).GetAsync($"/api/bookings/{_aSoon}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<BookingDto>(Json))!;
        Assert.Equal(("AAAA2222", "Dune", "Salong 1", 280m, BookingStatus.Confirmed),
            (dto.Reference, dto.MovieTitle, dto.Auditorium, dto.TotalPrice, dto.Status));
        Assert.Equal(DateTimeKind.Utc, dto.Start.Kind);
        Assert.Equal(DateTimeKind.Utc, dto.CreatedAt.Kind);
        Assert.Null(dto.CancelledAt);
        Assert.Equal(new[] { (1, 2, SeatType.Standard), (2, 1, SeatType.VIP) }, dto.Seats.Select(s => (s.Row, s.Number, s.Type)));
        Assert.Contains("Z", await response.Content.ReadAsStringAsync()); // the wire format carries the UTC marker
    }

    [Fact]
    public async Task Ac4_A_cancelled_booking_keeps_its_seats()
    {
        var dto = (await Client(CustomerA).GetFromJsonAsync<BookingDto>($"/api/bookings/{_aCancelled}", Json))!;

        Assert.Equal(BookingStatus.Cancelled, dto.Status);
        Assert.Single(dto.Seats);
    }

    [Fact]
    public async Task Ac5_Another_customers_booking_returns_403_without_booking_data()
    {
        var response = await Client(CustomerA).GetAsync($"/api/bookings/{_bSoon}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("BBBB2222", body);
        Assert.DoesNotContain("Dune", body);
    }

    [Fact]
    public async Task Ac5_An_unknown_id_returns_404_for_customers_and_staff()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client(CustomerA).GetAsync("/api/bookings/9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client(StaffId, Roles.Staff, Permissions.ManageBookings).GetAsync("/api/bookings/9999")).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Staff, Permissions.ManageBookings, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, null, HttpStatusCode.OK)]
    [InlineData(Roles.Staff, Permissions.ManageMovies, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, null, HttpStatusCode.Forbidden)]
    public async Task Ac6_Only_staff_with_ManageBookings_and_admin_can_read_any_booking_and_search(string role, string? permission, HttpStatusCode expected)
    {
        var permissions = permission is null ? [] : new[] { permission };
        var client = Client(StaffId, role, permissions);

        var detail = await client.GetAsync($"/api/bookings/{_aSoon}");
        var search = await client.GetAsync($"/api/bookings?showingId={_soon}");

        Assert.Equal(expected, detail.StatusCode);
        Assert.Equal(expected, search.StatusCode);
    }

    [Fact]
    public async Task Ac6_Search_filters_by_showing_and_reference_and_can_combine_them()
    {
        var client = Client(StaffId, Roles.Staff, Permissions.ManageBookings);

        var byShowing = await client.GetFromJsonAsync<List<BookingDto>>($"/api/bookings?showingId={_soon}", Json);
        var byReference = await client.GetFromJsonAsync<List<BookingDto>>("/api/bookings?reference=aaaa3333", Json);
        var both = await client.GetFromJsonAsync<List<BookingDto>>($"/api/bookings?showingId={_soon}&reference=AAAA3333", Json);
        var matching = await client.GetFromJsonAsync<List<BookingDto>>($"/api/bookings?showingId={_later}&reference=AAAA3333", Json);

        Assert.Equal(new[] { _bSoon, _aSoon }.Order(), byShowing!.Select(b => b.Id).Order());
        Assert.Equal(new[] { _aLater }, byReference!.Select(b => b.Id));
        Assert.Empty(both!);
        Assert.Equal(new[] { _aLater }, matching!.Select(b => b.Id));
    }

    [Theory]
    [InlineData("/api/bookings")]
    [InlineData("/api/bookings?reference=")]
    [InlineData("/api/bookings?reference=%20")]
    public async Task Ac6_Search_without_a_filter_returns_400(string url)
    {
        var response = await Client(StaffId, Roles.Staff, Permissions.ManageBookings).GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/bookings/mine?scope=upcoming")]
    [InlineData("/api/bookings/1")]
    [InlineData("/api/bookings?showingId=1")]
    public async Task Ac6_Anonymous_calls_return_401(string url)
    {
        var response = await Client(null).GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ac6_Staff_cannot_use_the_customer_only_mine_route()
    {
        var response = await Client(StaffId, Roles.Staff, Permissions.ManageBookings).GetAsync("/api/bookings/mine?scope=upcoming");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ac7_Mine_never_contains_another_users_bookings_even_on_the_same_showing()
    {
        var a = (await Mine("upcoming")).Concat(await Mine("history"));
        var b = (await Mine("upcoming", CustomerB)).Concat(await Mine("history", CustomerB));

        Assert.DoesNotContain(a, x => x.Id == _bSoon);
        Assert.Equal(new[] { _bSoon }, b.Select(x => x.Id));
    }
}
