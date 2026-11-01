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

/// <summary>US-132: public showings search and filter, run against a fixed clock.</summary>
public sealed class PublicBrowsingTests : IAsyncLifetime
{
    private const string Url = "/api/public/showings";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DateTime _now = new(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    private WebApplicationFactory<Program> _factory = null!;
    private int _salong1;
    private int _salong2;
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
        var a1 = new Auditorium { Name = "Salong 1", Rows = 1, SeatsPerRow = 3 };
        var a2 = new Auditorium { Name = "Salong 2", Rows = 1, SeatsPerRow = 2 };
        var seats = Enumerable.Range(1, 3)
            .Select(n => new Seat { Auditorium = a1, RowNumber = 1, SeatNumber = n, Type = SeatType.Standard }).ToArray();
        db.AddRange(a1, a2);
        db.AddRange(seats);
        db.AddRange(
            new Seat { Auditorium = a2, RowNumber = 1, SeatNumber = 1, Type = SeatType.Standard },
            new Seat { Auditorium = a2, RowNumber = 1, SeatNumber = 2, Type = SeatType.Standard });
        db.AddRange(
            NewMovie("Dune", Genre.SciFi, AgeRating.Age11, 175),
            NewMovie("Åsa-Nisse", Genre.Comedy, AgeRating.AllAges, 90),
            NewMovie("Nosferatu", Genre.Horror, AgeRating.Age15, 120),
            NewMovie("Räksmörgås", Genre.Drama, AgeRating.Age7, 100));
        db.Users.Add(new ApplicationUser { Id = "customer-1", UserName = "c@example.com", Email = "c@example.com", DisplayName = "Customer" });
        await db.SaveChangesAsync();
        (_salong1, _salong2) = (a1.Id, a2.Id);
        _seats = seats.Select(s => s.Id).ToArray();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static Movie NewMovie(string title, Genre genre, AgeRating rating, int minutes) =>
        new() { Title = title, Genre = genre, AgeRating = rating, LengthMinutes = minutes };

    /// <summary>Stockholm date of the fixed "now".</summary>
    private DateTime Today => TimeZoneInfo.ConvertTimeFromUtc(_now, Stockholm).Date;

    /// <summary>The UTC instant of a Stockholm wall-clock time <paramref name="days"/> days after today.</summary>
    private DateTime At(int days, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(Today.AddDays(days).AddHours(hour).AddMinutes(minute), Stockholm);

    private static string Day(DateTime localDate) => localDate.ToString("yyyy-MM-dd");

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> Seed(string title, int auditoriumId, DateTime start, ShowingStatus status = ShowingStatus.Scheduled) =>
        WithDb(async db =>
        {
            var movie = await db.Movies.SingleAsync(m => m.Title == title);
            var showing = new Showing
            {
                MovieId = movie.Id, AuditoriumId = auditoriumId, StartTime = start,
                EndTime = start.AddMinutes(movie.LengthMinutes + 15), Status = status
            };
            db.Showings.Add(showing);
            await db.SaveChangesAsync();
            return showing.Id;
        });

    private Task SeedBooking(int showingId, BookingStatus status, string reference, params int[] seatIds) =>
        WithDb(async db =>
        {
            db.Bookings.Add(new Booking
            {
                Reference = reference, UserId = "customer-1", ShowingId = showingId, TotalPrice = 100, Status = status,
                CreatedAt = _now,
                Seats = seatIds.Select(id => new BookingSeat
                {
                    ShowingId = showingId, SeatId = id, Price = 50, IsActive = status == BookingStatus.Confirmed
                }).ToList()
            });
            await db.SaveChangesAsync();
            return 0;
        });

    private async Task<HttpResponseMessage> Get(string url, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        using var client = _factory.CreateClient();
        return await client.SendAsync(request);
    }

    private async Task<ShowingListItem[]> List(string query = "")
    {
        var response = await Get(Url + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ShowingListItem[]>(Json))!;
    }

    private static string[] Titles(ShowingListItem[] items) => items.Select(i => i.Movie).ToArray();

    [Fact]
    public async Task Ac1_Default_list_is_anonymous_and_holds_the_showings_in_the_window_sorted_by_start_time()
    {
        var third = await Seed("Åsa-Nisse", _salong2, At(5, 18));
        var first = await Seed("Dune", _salong1, At(2, 12));
        var second = await Seed("Nosferatu", _salong1, At(3, 20));
        var lastInWindow = await Seed("Räksmörgås", _salong1, _now.AddDays(14)); // exactly now + 14 days is included

        var items = await List();

        Assert.Equal(new[] { first, second, third, lastInWindow }, items.Select(i => i.Id).ToArray());
        var dune = items[0];
        Assert.Equal(("Dune", "Sci-Fi", "11", 175, _salong1, "Salong 1", At(2, 12), At(2, 12).AddMinutes(190), 3),
            (dune.Movie, dune.Genre, dune.AgeRating, dune.LengthMinutes, dune.AuditoriumId, dune.Auditorium, dune.Start, dune.End, dune.FreeSeats));
        Assert.Equal(DateTimeKind.Utc, dune.Start.Kind);
        Assert.Equal("All ages", items[2].AgeRating);
    }

    [Fact]
    public async Task Ac1_An_empty_schedule_returns_200_with_an_empty_list()
    {
        Assert.Empty(await List());
    }

    [Theory]
    [InlineData("Horror", "Nosferatu")]
    [InlineData("horror", "Nosferatu")]
    [InlineData("Sci-Fi", "Dune")]
    [InlineData("scifi", "Dune")]
    public async Task Ac2_Genre_filter_returns_only_that_genre(string genre, string expected)
    {
        await SeedFourMovies();

        Assert.Equal(new[] { expected }, Titles(await List($"?genre={genre}")));
    }

    [Fact]
    public async Task Ac2_Date_filter_uses_the_display_time_zone_day()
    {
        await Seed("Dune", _salong1, At(2, 23, 30)); // 22:30 or 21:30 UTC, still the 2nd local day
        await Seed("Nosferatu", _salong1, At(3, 0, 30)); // 23:30 UTC on the 2nd day, but the 3rd local day
        await Seed("Åsa-Nisse", _salong1, At(4, 12));

        Assert.Equal(new[] { "Dune" }, Titles(await List($"?date={Day(Today.AddDays(2))}")));
        Assert.Equal(new[] { "Nosferatu" }, Titles(await List($"?date={Day(Today.AddDays(3))}")));
    }

    [Fact]
    public async Task Ac2_AuditoriumId_filter_returns_only_that_auditorium()
    {
        await Seed("Dune", _salong1, At(2, 12));
        var other = await Seed("Åsa-Nisse", _salong2, At(2, 14));

        var items = await List($"?auditoriumId={_salong2}");

        Assert.Equal(new[] { other }, items.Select(i => i.Id).ToArray());
    }

    [Theory]
    [InlineData("dun", "Dune")]
    [InlineData("DUN", "Dune")]
    [InlineData("ne", "Dune")]
    [InlineData("%20dun%20", "Dune")]
    [InlineData("%C3%85SA", "Åsa-Nisse")]
    [InlineData("%C3%A5sa", "Åsa-Nisse")]
    [InlineData("R%C3%84KSM", "Räksmörgås")]
    [InlineData("SM%C3%96RG%C3%85S", "Räksmörgås")]
    public async Task Ac2_Title_filter_matches_partly_and_ignores_case_including_swedish_letters(string title, string expected)
    {
        await SeedFourMovies();

        Assert.Equal(new[] { expected }, Titles(await List($"?title={title}")));
    }

    [Theory]
    [InlineData("%25")]
    [InlineData("_une")]
    public async Task Ac2_Title_wildcards_are_matched_literally(string title)
    {
        await SeedFourMovies();

        Assert.Empty(await List($"?title={title}"));
    }

    [Fact]
    public async Task Ac3_Combined_filters_return_only_showings_that_satisfy_all_of_them()
    {
        await WithDb(async db =>
        {
            db.Movies.Add(NewMovie("Dunkirk", Genre.Drama, AgeRating.Age11, 106));
            await db.SaveChangesAsync();
            return 0;
        });
        var match = await Seed("Dune", _salong1, At(2, 12));
        await Seed("Dune", _salong2, At(3, 12)); // wrong auditorium
        await Seed("Dunkirk", _salong1, At(4, 12)); // wrong genre
        await Seed("Nosferatu", _salong1, At(5, 12)); // wrong title

        var items = await List($"?genre=Sci-Fi&title=dun&auditoriumId={_salong1}");

        Assert.Equal(new[] { match }, items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task Ac4_Cancelled_past_and_beyond_14_days_showings_never_appear_even_when_the_filters_match()
    {
        await Seed("Dune", _salong1, At(2, 10), ShowingStatus.Cancelled);
        await Seed("Dune", _salong1, _now.AddMinutes(-1));
        await Seed("Dune", _salong1, _now); // start = now is not "after now"
        await Seed("Dune", _salong1, _now.AddDays(14).AddSeconds(1));
        var valid = await Seed("Dune", _salong1, At(3, 10));

        var items = await List($"?genre=Sci-Fi&title=dun&auditoriumId={_salong1}");

        Assert.Equal(new[] { valid }, items.Select(i => i.Id).ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(15)]
    [InlineData(400)]
    public async Task Ac4_A_date_outside_the_window_returns_200_with_an_empty_list(int daysFromToday)
    {
        await Seed("Dune", _salong1, At(2, 12));
        await Seed("Dune", _salong1, _now.AddDays(14).AddHours(1)); // matches the date but starts after the window

        Assert.Empty(await List($"?date={Day(Today.AddDays(daysFromToday))}"));
        Assert.Empty(await List($"?date={Day(_now.AddDays(14).AddHours(1))}&genre=Sci-Fi"));
    }

    [Fact]
    public async Task Ac5_FreeSeats_is_the_seat_count_minus_seats_of_confirmed_bookings_only()
    {
        var booked = await Seed("Dune", _salong1, At(2, 12));
        var empty = await Seed("Dune", _salong1, At(3, 12));
        var other = await Seed("Åsa-Nisse", _salong2, At(4, 12));
        await SeedBooking(booked, BookingStatus.Confirmed, "AAAAAAAA", _seats[0], _seats[1]);
        await SeedBooking(booked, BookingStatus.Cancelled, "BBBBBBBB", _seats[2]); // cancelled frees its seat
        await SeedBooking(empty, BookingStatus.Cancelled, "CCCCCCCC", _seats[0]);

        var free = (await List()).ToDictionary(i => i.Id, i => i.FreeSeats);

        Assert.Equal(1, free[booked]);
        Assert.Equal(3, free[empty]);
        Assert.Equal(2, free[other]);
    }

    [Theory]
    [InlineData("date=abc")]
    [InlineData("date=2026-13-45")]
    [InlineData("date=03-11-2026")]
    [InlineData("auditoriumId=x")]
    public async Task Ac6_Invalid_date_or_auditoriumId_returns_400_problem_details(string query)
    {
        var response = await Get($"{Url}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Ac6_A_title_longer_than_100_characters_returns_400()
    {
        var response = await Get($"{Url}?title={new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Ac6_Unknown_genre_or_auditorium_returns_200_with_an_empty_list_and_empty_filters_are_ignored()
    {
        await Seed("Dune", _salong1, At(2, 12));

        Assert.Empty(await List("?genre=Western"));
        Assert.Empty(await List("?auditoriumId=9999"));
        Assert.Single(await List("?genre=&title=&date="));
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData(Roles.Customer, 403)]
    public async Task Ac7_Staff_showings_route_still_requires_ManageShowings(string? role, int expected)
    {
        var token = role is null
            ? null
            : _factory.Services.GetRequiredService<TokenService>().Create(
                new ApplicationUser { Id = "caller-1", Email = "caller@example.com", DisplayName = "Caller" }, role, []).Token;

        var response = await Get("/api/showings?from=2000-01-01&to=2100-01-01", token);

        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Fact]
    public async Task Public_genres_and_auditoriums_are_anonymous_and_sorted()
    {
        var genres = await Get("/api/public/genres");
        var auditoriums = await Get("/api/public/auditoriums");

        Assert.Equal(new[] { "Comedy", "Drama", "Horror", "Sci-Fi" }, await genres.Content.ReadFromJsonAsync<string[]>(Json));
        Assert.Equal(new[] { new AuditoriumOption(_salong1, "Salong 1"), new AuditoriumOption(_salong2, "Salong 2") },
            await auditoriums.Content.ReadFromJsonAsync<AuditoriumOption[]>(Json));
    }

    private async Task SeedFourMovies()
    {
        await Seed("Dune", _salong1, At(2, 12));
        await Seed("Åsa-Nisse", _salong1, At(3, 12));
        await Seed("Nosferatu", _salong1, At(4, 12));
        await Seed("Räksmörgås", _salong1, At(5, 12));
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
