using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Events;
using NewtonsBiograf.Api.Pricing;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

/// <summary>US-135: create a booking. Uses a temporary file database so that concurrent requests really race.</summary>
public sealed class BookingCreateTests : IAsyncLifetime
{
    private const string Url = "/api/bookings";
    private const string UserId = "customer-1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StrictEnumConverterFactory() }
    };

    private static DateTime Future => new(DateTime.UtcNow.Year + 1, 1, 15, 17, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nb-booking-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private int _auditorium;
    private int _s1;
    private int _s2;
    private int _s3;
    private int _v1;
    private int _o1;

    private string ConnectionString => $"Data Source={_dbPath}";

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseSqlite(ConnectionString));
            }));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var a = new Auditorium { Name = "Salong 1", Rows = 2, SeatsPerRow = 3 };
        var other = new Auditorium { Name = "Salong 2", Rows = 1, SeatsPerRow = 1 };
        var s1 = new Seat { Auditorium = a, RowNumber = 1, SeatNumber = 1, Type = SeatType.Standard };
        var s2 = new Seat { Auditorium = a, RowNumber = 1, SeatNumber = 2, Type = SeatType.Standard };
        var s3 = new Seat { Auditorium = a, RowNumber = 1, SeatNumber = 3, Type = SeatType.Standard };
        var v1 = new Seat { Auditorium = a, RowNumber = 2, SeatNumber = 1, Type = SeatType.VIP };
        var o1 = new Seat { Auditorium = other, RowNumber = 1, SeatNumber = 1, Type = SeatType.Standard };
        db.AddRange(a, other, s1, s2, s3, v1, o1);
        db.Movies.Add(new Movie { Title = "Dune", Genre = Genre.SciFi, AgeRating = AgeRating.Age11, LengthMinutes = 100 });
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = "c@example.com", Email = "c@example.com", DisplayName = "Customer" });
        await db.SaveChangesAsync();
        (_auditorium, _s1, _s2, _s3, _v1, _o1) = (a.Id, s1.Id, s2.Id, s3.Id, v1.Id, o1.Id);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private string Token(string role) =>
        _factory.Services.GetRequiredService<TokenService>().Create(
            new ApplicationUser { Id = UserId, Email = "c@example.com", DisplayName = "Customer" }, role, []).Token;

    private async Task<HttpResponseMessage> BookBody(object body, string? role = Roles.Customer, WebApplicationFactory<Program>? factory = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(body) };
        if (role is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(role));
        }
        using var client = (factory ?? _factory).CreateClient();
        return await client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Book(int showingId, params int[] seatIds) =>
        BookBody(new { ShowingId = showingId, SeatIds = seatIds });

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> SeedShowing(DateTime? start = null, ShowingStatus status = ShowingStatus.Scheduled) =>
        WithDb(async db =>
        {
            var begin = start ?? Future;
            var showing = new Showing
            {
                MovieId = await db.Movies.Select(m => m.Id).SingleAsync(), AuditoriumId = _auditorium,
                StartTime = begin, EndTime = begin.AddMinutes(115), Status = status
            };
            db.Showings.Add(showing);
            await db.SaveChangesAsync();
            return showing.Id;
        });

    private Task<(int Bookings, int Seats)> Counts() =>
        WithDb(async db => (await db.Bookings.CountAsync(), await db.BookingSeats.CountAsync()));

    private static async Task<T> Read<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(Json))!;

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(detail, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Ac1_A_customer_gets_201_with_location_and_the_booking_dto()
    {
        var showing = await SeedShowing();

        var response = await Book(showing, _v1, _s2); // sent out of order, returned by row then number

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await Read<BookingDto>(response);
        Assert.Equal($"/api/bookings/{dto.Id}", response.Headers.Location!.OriginalString);
        Assert.Equal(BookingStatus.Confirmed, dto.Status);
        Assert.Equal(340m, dto.TotalPrice);
        Assert.Equal(("Dune", "Salong 1", Future), (dto.MovieTitle, dto.Auditorium, dto.Start));
        Assert.Equal(DateTimeKind.Utc, dto.Start.Kind);
        Assert.Equal(
            new[] { new BookingSeatDto(1, 2, SeatType.Standard, 140), new BookingSeatDto(2, 1, SeatType.VIP, 200) },
            dto.Seats);
        var saved = await WithDb(db => db.Bookings.Include(b => b.Seats).SingleAsync());
        Assert.Equal((dto.Id, dto.Reference, UserId, showing, BookingStatus.Confirmed, 340d),
            (saved.Id, saved.Reference, saved.UserId, saved.ShowingId, saved.Status, (double)saved.TotalPrice));
        Assert.Equal(new[] { _s2, _v1 }.Order(), saved.Seats.Select(s => s.SeatId).Order());
        Assert.All(saved.Seats, s => Assert.True(s.IsActive));
    }

    [Fact]
    public async Task Ac2_Two_standard_and_one_VIP_seat_cost_480_with_the_default_prices()
    {
        var showing = await SeedShowing();

        var dto = await Read<BookingDto>(await Book(showing, _s1, _s2, _v1));

        Assert.Equal(2 * 140 + 200, dto.TotalPrice);
        Assert.Equal(480m, (await WithDb(db => db.Bookings.SingleAsync())).TotalPrice);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("nine")]
    [InlineData("duplicate")]
    [InlineData("other auditorium")]
    [InlineData("unknown seat")]
    [InlineData("missing")]
    public async Task Ac3_An_invalid_seat_selection_returns_400_keyed_by_SeatIds_and_saves_nothing(string selection)
    {
        var showing = await SeedShowing();
        var body = selection switch
        {
            "empty" => new { ShowingId = showing, SeatIds = Array.Empty<int>() },
            "nine" => new { ShowingId = showing, SeatIds = Enumerable.Range(1, 9).ToArray() },
            "duplicate" => new { ShowingId = showing, SeatIds = new[] { _s1, _s1 } },
            "other auditorium" => new { ShowingId = showing, SeatIds = new[] { _s1, _o1 } },
            "unknown seat" => new { ShowingId = showing, SeatIds = new[] { 9999 } },
            _ => new { ShowingId = showing, SeatIds = (int[])null! }
        };

        var response = await BookBody(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.NotEmpty(problem.Errors["SeatIds"]);
        Assert.Equal((0, 0), await Counts());
    }

    [Fact]
    public async Task Ac3_Eight_seats_are_allowed()
    {
        var showing = await SeedShowing();
        var seats = await WithDb(async db =>
        {
            db.Seats.AddRange(Enumerable.Range(4, 4)
                .Select(n => new Seat { AuditoriumId = _auditorium, RowNumber = 3, SeatNumber = n, Type = SeatType.Standard }));
            await db.SaveChangesAsync();
            return await db.Seats.Where(s => s.AuditoriumId == _auditorium).Select(s => s.Id).ToArrayAsync();
        });

        var response = await Book(showing, seats);

        Assert.Equal(8, seats.Length);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((1, 8), await Counts());
    }

    [Fact]
    public async Task Ac4_An_unknown_showing_returns_404()
    {
        var response = await Book(9999, _s1);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal((0, 0), await Counts());
    }

    [Theory]
    [InlineData(ShowingStatus.Cancelled, 1)]
    [InlineData(ShowingStatus.Scheduled, -1)]
    public async Task Ac4_A_cancelled_or_started_showing_returns_409_and_saves_nothing(ShowingStatus status, int days)
    {
        var showing = await SeedShowing(DateTime.UtcNow.AddDays(days), status);

        var response = await Book(showing, _s1);

        await AssertProblem(response, HttpStatusCode.Conflict, "This showing can no longer be booked.");
        Assert.Equal((0, 0), await Counts());
    }

    [Fact]
    public async Task Ac5_A_seat_that_is_already_booked_returns_409_with_the_current_seat_map_and_saves_no_part_of_the_booking()
    {
        var showing = await SeedShowing();
        Assert.Equal(HttpStatusCode.Created, (await Book(showing, _s1, _s2)).StatusCode);

        var response = await Book(showing, _s2, _v1);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await Read<SeatConflictDto>(response);
        Assert.Equal("One or more seats are no longer available", conflict.Message);
        Assert.Equal(new[] { _s1, _s2 }.Order(), conflict.SeatMap.Seats.Where(s => s.IsTaken).Select(s => s.SeatId).Order());
        Assert.Equal((1, 2), await Counts());
    }

    [Fact]
    public async Task Ac5_The_unique_index_rejects_a_seat_taken_after_the_pre_check_and_rolls_the_whole_booking_back()
    {
        var showing = await SeedShowing();
        var rival = new RivalBookingInterceptor(ConnectionString, showing, _s1);
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(ConnectionString).AddInterceptors(rival));
        }));

        var response = await BookBody(new { ShowingId = showing, SeatIds = new[] { _s1, _v1 } }, factory: factory);

        Assert.True(rival.Fired);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await Read<SeatConflictDto>(response);
        Assert.Equal("One or more seats are no longer available", conflict.Message);
        Assert.Equal(new[] { _s1 }, conflict.SeatMap.Seats.Where(s => s.IsTaken).Select(s => s.SeatId));
        Assert.Equal((1, 1), await Counts()); // only the rival booking
    }

    [Fact]
    public async Task Ac6_Two_simultaneous_requests_for_the_same_seat_give_exactly_one_201_and_one_409()
    {
        for (var round = 1; round <= 5; round++)
        {
            var showing = await SeedShowing(Future.AddHours(round));

            var responses = await Task.WhenAll(Book(showing, _s1, _s2), Book(showing, _s2, _s3));

            Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, responses.Select(r => r.StatusCode).Order());
            var conflict = await Read<SeatConflictDto>(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.True(conflict.SeatMap.Seats.Single(s => s.SeatId == _s2).IsTaken);
            var winner = await Read<BookingDto>(responses.Single(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(winner.Seats.Count, conflict.SeatMap.Seats.Count(s => s.IsTaken));
            Assert.Equal((round, round * 2), await Counts());
        }
    }

    [Fact]
    public async Task Ac7_BookingCreated_is_raised_once_after_a_successful_booking()
    {
        var showing = await SeedShowing();
        var raised = Subscribe();

        var dto = await Read<BookingDto>(await Book(showing, _s1));

        var e = Assert.Single(raised);
        Assert.Equal((dto.Id, UserId, showing), (e.BookingId, e.UserId, e.ShowingId));
    }

    [Fact]
    public async Task Ac7_No_event_is_raised_when_the_booking_fails()
    {
        var showing = await SeedShowing();
        await Book(showing, _s1);
        var raised = Subscribe();

        await Book(showing, _s1); // conflict
        await Book(showing, Array.Empty<int>()); // invalid
        await Book(9999, _s2); // unknown showing

        Assert.Empty(raised);
    }

    [Fact]
    public async Task Ac7_A_failing_subscriber_does_not_fail_the_booking_or_stop_the_other_subscribers()
    {
        var showing = await SeedShowing();
        _factory.Services.GetRequiredService<IBookingEventPublisher>().BookingCreated +=
            (_, _) => throw new InvalidOperationException("subscriber failed");
        var raised = Subscribe();

        var response = await Book(showing, _s1);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(raised);
        Assert.Equal((1, 1), await Counts());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Staff, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Admin, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Customer, HttpStatusCode.Created)]
    public async Task Ac8_Only_a_customer_can_book(string? role, HttpStatusCode expected)
    {
        var showing = await SeedShowing();

        var response = await BookBody(new { ShowingId = showing, SeatIds = new[] { _s1 } }, role);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.Created ? (1, 1) : (0, 0), await Counts());
    }

    [Fact]
    public async Task Ac9_A_reference_has_8_characters_from_the_unambiguous_alphabet()
    {
        Assert.Equal("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", BookingService.ReferenceAlphabet);
        var pattern = new Regex("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$");
        var references = new List<string>();

        for (var i = 1; i <= 10; i++)
        {
            var showing = await SeedShowing(Future.AddHours(i));
            references.Add((await Read<BookingDto>(await Book(showing, _s1))).Reference);
        }

        Assert.All(references, r => Assert.Matches(pattern, r));
        Assert.Equal(10, references.Distinct().Count());
    }

    [Fact]
    public async Task Ac9_A_duplicate_reference_is_regenerated_and_the_booking_still_succeeds()
    {
        var taken = await SeedShowing();
        await Book(taken, _s1);
        var existing = await WithDb(db => db.Bookings.Select(b => b.Reference).SingleAsync());
        var next = await SeedShowing(Future.AddHours(3));
        var offered = new List<string>();
        var factory = WithReferences(offered, existing, existing, "BBBBBBBB");

        var response = await BookBody(new { ShowingId = next, SeatIds = new[] { _s1 } }, factory: factory);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("BBBBBBBB", (await Read<BookingDto>(response)).Reference);
        Assert.Equal(3, offered.Count);
        Assert.Equal((2, 2), await Counts());
    }

    [Fact]
    public async Task Ac9_After_five_retries_with_a_duplicate_reference_the_request_fails_and_saves_nothing()
    {
        var taken = await SeedShowing();
        await Book(taken, _s1);
        var existing = await WithDb(db => db.Bookings.Select(b => b.Reference).SingleAsync());
        var next = await SeedShowing(Future.AddHours(3));
        var offered = new List<string>();
        var factory = WithReferences(offered, Enumerable.Repeat(existing, 10).ToArray());
        var raised = Subscribe();

        var response = await BookBody(new { ShowingId = next, SeatIds = new[] { _s1 } }, factory: factory);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(6, offered.Count); // the first attempt plus 5 retries
        Assert.Equal((1, 1), await Counts());
        Assert.Empty(raised);
    }

    [Fact]
    public async Task Ac10_The_seats_of_a_cancelled_booking_can_be_booked_again()
    {
        var showing = await SeedShowing();
        var first = await Read<BookingDto>(await Book(showing, _s1, _s2));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var booking = await db.Bookings.Include(b => b.Seats).SingleAsync(b => b.Id == first.Id);
            await scope.ServiceProvider.GetRequiredService<BookingService>().CancelInternalAsync(booking);
            await db.SaveChangesAsync();
        }

        var again = await Book(showing, _s1, _s2);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal((2, 4), await Counts());
        Assert.Equal(2, await WithDb(db => db.BookingSeats.CountAsync(s => s.IsActive)));
    }

    private List<BookingEventArgs> Subscribe()
    {
        var raised = new List<BookingEventArgs>();
        _factory.Services.GetRequiredService<IBookingEventPublisher>().BookingCreated += (_, e) =>
        {
            lock (raised)
            {
                raised.Add(e);
            }
        };
        return raised;
    }

    /// <summary>A factory whose service offers the given references in order and records each one in <paramref name="offered"/>.</summary>
    private WebApplicationFactory<Program> WithReferences(List<string> offered, params string[] references)
    {
        var queue = new Queue<string>(references);
        return _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<BookingService>();
            services.AddScoped<BookingService>(sp => new ScriptedReferenceBookingService(
                sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<IPriceCalculator>(),
                sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IBookingEventPublisher>(),
                () =>
                {
                    var reference = queue.Dequeue();
                    offered.Add(reference);
                    return reference;
                }));
        }));
    }

    private sealed class ScriptedReferenceBookingService(AppDbContext db, IPriceCalculator prices, TimeProvider clock,
        IBookingEventPublisher publisher, Func<string> next) : BookingService(db, prices, clock, publisher)
    {
        public override string NewReference() => next();
    }

    /// <summary>Books a seat for someone else right before the booking is saved, after the availability pre-check passed.</summary>
    private sealed class RivalBookingInterceptor(string connectionString, int showingId, int seatId) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await using var rival = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options);
                rival.Bookings.Add(new Booking
                {
                    Reference = "RIVAL222", UserId = UserId, ShowingId = showingId, TotalPrice = 140,
                    Status = BookingStatus.Confirmed, CreatedAt = DateTime.UtcNow,
                    Seats = [new BookingSeat { ShowingId = showingId, SeatId = seatId, Price = 140 }]
                });
                await rival.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }
}
