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
using NewtonsBiograf.Api.Events;
using NewtonsBiograf.Api.Pricing;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

public sealed class ShowingTests : IAsyncLifetime
{
    private const string Url = "/api/showings";
    private const int MovieLength = 175;
    private const int Slot = MovieLength + 15;
    private const int ShortMovieLength = 100;
    private const string BlockedEditMessage =
        "Showing has bookings or has started and cannot be edited. Cancel it and create a new one.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StrictEnumConverterFactory() }
    };

    /// <summary>15 January next year, 17:00 UTC = 18:00 in Stockholm (winter time); always in the future.</summary>
    private static DateTime Winter => new(DateTime.UtcNow.Year + 1, 1, 15, 17, 0, 0, DateTimeKind.Utc);

    /// <summary>15 July next year, 15:00 UTC = 17:00 in Stockholm (summer time); always in the future.</summary>
    private static DateTime Summer => new(DateTime.UtcNow.Year + 1, 7, 15, 15, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly RecordingEvents _events = new();
    private WebApplicationFactory<Program> _factory = null!;
    private int _movieId;
    private int _shortMovieId;
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
                services.RemoveAll<IBookingEvents>();
                services.AddSingleton<IBookingEvents>(_events);
            }));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var movie = new Movie { Title = "Dune", Genre = Genre.SciFi, AgeRating = AgeRating.Age11, LengthMinutes = MovieLength };
        var shortMovie = new Movie { Title = "Short", Genre = Genre.Comedy, AgeRating = AgeRating.AllAges, LengthMinutes = ShortMovieLength };
        var a1 = new Auditorium { Name = "Salong 1", Rows = 1, SeatsPerRow = 3 };
        var a2 = new Auditorium { Name = "Salong 2", Rows = 1, SeatsPerRow = 1 };
        var seats = Enumerable.Range(1, 3)
            .Select(n => new Seat { Auditorium = a1, RowNumber = 1, SeatNumber = n, Type = SeatType.Standard }).ToArray();
        db.AddRange(movie, shortMovie, a1, a2);
        db.AddRange(seats);
        db.Users.Add(new ApplicationUser { Id = "customer-1", UserName = "c@example.com", Email = "c@example.com", DisplayName = "Customer" });
        await db.SaveChangesAsync();
        (_movieId, _shortMovieId, _salong1, _salong2) = (movie.Id, shortMovie.Id, a1.Id, a2.Id);
        _seats = seats.Select(s => s.Id).ToArray();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private string Token(string role, params string[] permissions) =>
        _factory.Services.GetRequiredService<TokenService>().Create(
            new ApplicationUser { Id = "caller-1", Email = "caller@example.com", DisplayName = "Caller" }, role, permissions).Token;

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null, string? token = "admin",
        WebApplicationFactory<Program>? factory = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token == "admin" ? Token(Roles.Admin) : token);
        }
        using var client = (factory ?? _factory).CreateClient();
        return await client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Post(object body, string? token = "admin") => Send(HttpMethod.Post, Url, body, token);

    private Task<HttpResponseMessage> Create(DateTime start, int? auditoriumId = null, string? token = "admin") =>
        Post(new { MovieId = _movieId, AuditoriumId = auditoriumId ?? _salong1, StartTime = start }, token);

    private Task<HttpResponseMessage> Edit(int id, DateTime start, int? auditoriumId = null, int? movieId = null) =>
        Send(HttpMethod.Put, $"{Url}/{id}",
            new { MovieId = movieId ?? _movieId, AuditoriumId = auditoriumId ?? _salong1, StartTime = start });

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> ShowingCount() => WithDb(db => db.Showings.CountAsync());

    private Task<Showing> LoadShowing(int id) => WithDb(db => db.Showings.AsNoTracking().SingleAsync(s => s.Id == id));

    private Task<List<Booking>> LoadBookings() =>
        WithDb(db => db.Bookings.AsNoTracking().Include(b => b.Seats).OrderBy(b => b.Id).ToListAsync());

    /// <summary>Seeds a showing of the movie lasting <paramref name="minutes"/> (no cleaning time) so the end is explicit.</summary>
    private Task<int> SeedShowing(DateTime start, int auditoriumId, ShowingStatus status = ShowingStatus.Scheduled, int minutes = MovieLength) =>
        WithDb(async db =>
        {
            var showing = new Showing
            {
                MovieId = _movieId, AuditoriumId = auditoriumId, StartTime = start, EndTime = start.AddMinutes(minutes), Status = status
            };
            db.Showings.Add(showing);
            await db.SaveChangesAsync();
            return showing.Id;
        });

    /// <summary>Seeds a booking of one seat; its BookingSeat is active only while the booking is Confirmed.</summary>
    private Task<int> SeedBooking(int showingId, int seatId, BookingStatus status, string reference) =>
        WithDb(async db =>
        {
            var booking = new Booking
            {
                Reference = reference, UserId = "customer-1", ShowingId = showingId, TotalPrice = 100, Status = status,
                CreatedAt = DateTime.UtcNow,
                Seats = [new BookingSeat { ShowingId = showingId, SeatId = seatId, Price = 100, IsActive = status == BookingStatus.Confirmed }]
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            return booking.Id;
        });

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        return new Dictionary<string, string[]>(problem!.Errors, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(detail, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Ac1_Create_returns_201_with_location_and_a_scheduled_showing_ending_after_length_plus_cleaning()
    {
        var start = Winter;

        var response = await Create(start);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<ShowingDto>(Json))!;
        Assert.Equal($"{Url}/{dto.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal(ShowingStatus.Scheduled, dto.Status);
        Assert.Equal(start, dto.StartTime);
        Assert.Equal(start.AddMinutes(Slot), dto.EndTime);
        Assert.Equal((_movieId, "Dune", _salong1, "Salong 1", 0), (dto.MovieId, dto.MovieTitle, dto.AuditoriumId, dto.AuditoriumName, dto.ActiveBookingCount));
        var saved = await WithDb(db => db.Showings.SingleAsync());
        Assert.Equal((start, start.AddMinutes(Slot), ShowingStatus.Scheduled), (saved.StartTime, saved.EndTime, saved.Status));
    }

    [Fact]
    public async Task Ac1_Create_converts_an_offset_start_to_UTC_and_the_location_can_be_read_back()
    {
        var response = await Post(new { MovieId = _movieId, AuditoriumId = _salong1, StartTime = "2090-01-15T19:00:00+02:00" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<ShowingDto>(Json))!;
        Assert.Equal(new DateTime(2090, 1, 15, 17, 0, 0, DateTimeKind.Utc), dto.StartTime);

        var read = await Send(HttpMethod.Get, response.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(dto, await read.Content.ReadFromJsonAsync<ShowingDto>(Json));
    }

    [Fact]
    public async Task Ac2_A_start_in_the_past_returns_400_keyed_by_StartTime_and_saves_nothing()
    {
        var response = await Create(DateTime.UtcNow.AddMinutes(-1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))["StartTime"]);
        Assert.Equal(0, await ShowingCount());
    }

    [Theory]
    [InlineData("2090-01-15T18:00:00")]
    [InlineData(null)]
    public async Task Ac2_A_start_without_UTC_offset_or_missing_returns_400_keyed_by_StartTime(string? start)
    {
        var body = start is null
            ? (object)new { MovieId = _movieId, AuditoriumId = _salong1 }
            : new { MovieId = _movieId, AuditoriumId = _salong1, StartTime = start };

        var response = await Post(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))["StartTime"]);
        Assert.Equal(0, await ShowingCount());
    }

    [Theory]
    [InlineData(-Slot + 1)] // new end is one minute after the existing start
    [InlineData(-100)]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(MovieLength - 1)] // new start is one minute before the existing end
    public async Task Ac3_An_overlapping_start_returns_409_naming_the_conflicting_showing_and_saves_nothing(int offsetMinutes)
    {
        await SeedShowing(Winter, _salong1);

        var response = await Create(Winter.AddMinutes(offsetMinutes));

        await AssertProblem(response, HttpStatusCode.Conflict, "Overlaps with 'Dune' 18:00–20:55 in Salong 1");
        Assert.Equal(1, await ShowingCount());
    }

    [Fact]
    public async Task Ac3_The_conflict_detail_uses_the_display_time_zone_with_daylight_saving_time()
    {
        await SeedShowing(Summer, _salong1);

        var response = await Create(Summer.AddMinutes(30));

        await AssertProblem(response, HttpStatusCode.Conflict, "Overlaps with 'Dune' 17:00–19:55 in Salong 1");
    }

    [Fact]
    public async Task Ac4_The_same_time_in_another_auditorium_returns_201()
    {
        await SeedShowing(Winter, _salong1);

        var response = await Create(Winter, _salong2);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, await ShowingCount());
    }

    [Theory]
    [InlineData(MovieLength)] // new start equals the existing end
    [InlineData(-Slot)] // new end equals the existing start
    public async Task Ac5_Back_to_back_showings_return_201(int offsetMinutes)
    {
        await SeedShowing(Winter, _salong1);

        var response = await Create(Winter.AddMinutes(offsetMinutes));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, await ShowingCount());
    }

    [Fact]
    public async Task Ac6_A_cancelled_overlapping_showing_does_not_block_but_a_scheduled_one_still_does()
    {
        await SeedShowing(Winter, _salong1, ShowingStatus.Cancelled);

        var created = await Create(Winter);
        var blocked = await Create(Winter.AddMinutes(10));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(2, await ShowingCount());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Ac7_Unknown_movie_or_auditorium_returns_400_with_an_error_on_each_unknown_field(bool badMovie, bool badAuditorium)
    {
        var response = await Post(new
        {
            MovieId = badMovie ? 9999 : _movieId,
            AuditoriumId = badAuditorium ? 9999 : _salong1,
            StartTime = Winter
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await Errors(response);
        Assert.Equal(badMovie, errors.ContainsKey("MovieId"));
        Assert.Equal(badAuditorium, errors.ContainsKey("AuditoriumId"));
        Assert.Equal(0, await ShowingCount());
    }

    [Theory]
    [InlineData(null, null, 401)]
    [InlineData(Roles.Customer, null, 403)]
    [InlineData(Roles.Staff, null, 403)]
    [InlineData(Roles.Staff, Permissions.ManageMovies, 403)]
    [InlineData(Roles.Staff, Permissions.ManageShowings, 201)]
    [InlineData(Roles.Admin, null, 201)]
    public async Task Ac8_Create_by_caller(string? role, string? permission, int expected)
    {
        var token = role is null ? null : Token(role, permission is null ? Array.Empty<string>() : new[] { permission });

        var response = await Create(Winter, token: token);

        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(expected == 201 ? 1 : 0, await ShowingCount());
    }

    // ---- US-130: edit, cancel, delete ----

    [Fact]
    public async Task Us130_Ac1_Edit_updates_movie_auditorium_and_start_and_recalculates_the_end_time()
    {
        var id = await SeedShowing(Winter, _salong1);

        var response = await Edit(id, Summer, _salong2, _shortMovieId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var saved = await LoadShowing(id);
        Assert.Equal((_shortMovieId, _salong2, Summer, Summer.AddMinutes(ShortMovieLength + 15), ShowingStatus.Scheduled),
            (saved.MovieId, saved.AuditoriumId, saved.StartTime, saved.EndTime, saved.Status));
    }

    [Fact]
    public async Task Us130_Ac1_Edit_with_a_start_in_the_past_returns_400_on_StartTime_and_changes_nothing()
    {
        var id = await SeedShowing(Winter, _salong1);

        var response = await Edit(id, DateTime.UtcNow.AddMinutes(-1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))["StartTime"]);
        Assert.Equal(Winter, (await LoadShowing(id)).StartTime);
    }

    [Fact]
    public async Task Us130_Ac1_Edit_overlapping_another_showing_returns_409_but_the_showing_itself_is_ignored()
    {
        var id = await SeedShowing(Winter, _salong1);
        await SeedShowing(Summer, _salong1);

        var blocked = await Edit(id, Summer.AddMinutes(30));
        var shifted = await Edit(id, Winter.AddMinutes(30)); // overlaps only its own old slot

        await AssertProblem(blocked, HttpStatusCode.Conflict, "Overlaps with 'Dune' 17:00–19:55 in Salong 1");
        Assert.Equal(HttpStatusCode.NoContent, shifted.StatusCode);
        Assert.Equal(Winter.AddMinutes(30), (await LoadShowing(id)).StartTime);
    }

    [Fact]
    public async Task Us130_Ac2_Cancel_cancels_the_showing_and_every_confirmed_booking_and_frees_the_seats()
    {
        var id = await SeedShowing(Winter, _salong1);
        var other = await SeedShowing(Summer, _salong1);
        await SeedBooking(id, _seats[0], BookingStatus.Confirmed, "AAAAAAAA");
        await SeedBooking(id, _seats[1], BookingStatus.Confirmed, "BBBBBBBB");
        await SeedBooking(id, _seats[2], BookingStatus.Cancelled, "CCCCCCCC");
        await SeedBooking(other, _seats[0], BookingStatus.Confirmed, "DDDDDDDD");

        var response = await Send(HttpMethod.Post, $"{Url}/{id}/cancel");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ShowingStatus.Cancelled, (await LoadShowing(id)).Status);
        Assert.Equal(ShowingStatus.Scheduled, (await LoadShowing(other)).Status);
        var bookings = (await LoadBookings()).ToDictionary(b => b.Reference);
        Assert.All(new[] { "AAAAAAAA", "BBBBBBBB", "CCCCCCCC" }, r =>
        {
            Assert.Equal(BookingStatus.Cancelled, bookings[r].Status);
            Assert.All(bookings[r].Seats, s => Assert.False(s.IsActive));
        });
        Assert.NotNull(bookings["AAAAAAAA"].CancelledAt);
        Assert.Null(bookings["CCCCCCCC"].CancelledAt); // already cancelled earlier: untouched
        Assert.Equal(BookingStatus.Confirmed, bookings["DDDDDDDD"].Status);
        Assert.All(bookings["DDDDDDDD"].Seats, s => Assert.True(s.IsActive));
    }

    [Fact]
    public async Task Us130_Ac2_The_seats_of_a_cancelled_booking_can_be_booked_again_by_the_database_rules()
    {
        var id = await SeedShowing(Winter, _salong1);
        await SeedBooking(id, _seats[0], BookingStatus.Confirmed, "AAAAAAAA");
        await Send(HttpMethod.Post, $"{Url}/{id}/cancel");

        var again = await SeedBooking(id, _seats[0], BookingStatus.Confirmed, "EEEEEEEE");

        Assert.True(again > 0);
    }

    [Fact]
    public async Task Us130_Ac2_Cancelling_an_already_cancelled_showing_returns_204_without_events()
    {
        var id = await SeedShowing(Winter, _salong1);
        await SeedBooking(id, _seats[0], BookingStatus.Confirmed, "AAAAAAAA");
        await Send(HttpMethod.Post, $"{Url}/{id}/cancel");

        var again = await Send(HttpMethod.Post, $"{Url}/{id}/cancel");

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Single(_events.Received);
    }

    [Fact]
    public async Task Us130_Ac2_A_showing_that_has_already_started_can_be_cancelled()
    {
        var id = await SeedShowing(DateTime.UtcNow.AddMinutes(-30), _salong1);
        await SeedBooking(id, _seats[0], BookingStatus.Confirmed, "AAAAAAAA");

        var response = await Send(HttpMethod.Post, $"{Url}/{id}/cancel");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ShowingStatus.Cancelled, (await LoadShowing(id)).Status);
    }

    [Fact]
    public async Task Us130_Ac3_Delete_removes_a_showing_without_bookings()
    {
        var id = await SeedShowing(Winter, _salong1);

        var response = await Send(HttpMethod.Delete, $"{Url}/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await ShowingCount());
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task Us130_Ac3_Delete_with_any_booking_returns_409_and_keeps_the_showing(BookingStatus status)
    {
        var id = await SeedShowing(Winter, _salong1);
        await SeedBooking(id, _seats[0], status, "AAAAAAAA");

        var response = await Send(HttpMethod.Delete, $"{Url}/{id}");

        await AssertProblem(response, HttpStatusCode.Conflict, "Showing has bookings and can only be cancelled.");
        Assert.Equal(1, await ShowingCount());
    }

    [Fact]
    public async Task Us130_Ac4_A_failing_booking_cancellation_rolls_everything_back_and_raises_no_events()
    {
        var id = await SeedShowing(Winter, _salong1);
        await SeedBooking(id, _seats[0], BookingStatus.Confirmed, "AAAAAAAA");
        await SeedBooking(id, _seats[1], BookingStatus.Confirmed, "BBBBBBBB");
        var failing = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<BookingService>();
            services.AddScoped<BookingService, FailingOnSecondBookingService>();
        }));

        var response = await Send(HttpMethod.Post, $"{Url}/{id}/cancel", factory: failing);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ShowingStatus.Scheduled, (await LoadShowing(id)).Status);
        var bookings = await LoadBookings();
        Assert.All(bookings, b =>
        {
            Assert.Equal(BookingStatus.Confirmed, b.Status);
            Assert.All(b.Seats, s => Assert.True(s.IsActive));
        });
        Assert.Empty(_events.Received);
    }

    [Fact]
    public async Task Us130_Ac5_Edit_is_blocked_with_409_when_the_showing_has_a_confirmed_booking()
    {
        var id = await SeedShowing(Winter, _salong1);
        await SeedBooking(id, _seats[0], BookingStatus.Confirmed, "AAAAAAAA");

        var response = await Edit(id, Summer);

        await AssertProblem(response, HttpStatusCode.Conflict, BlockedEditMessage);
        Assert.Equal(Winter, (await LoadShowing(id)).StartTime);
    }

    [Fact]
    public async Task Us130_Ac5_Edit_is_blocked_with_409_when_the_showing_has_already_started()
    {
        var started = DateTime.UtcNow.AddMinutes(-30);
        var id = await SeedShowing(started, _salong1);

        var response = await Edit(id, Summer);

        await AssertProblem(response, HttpStatusCode.Conflict, BlockedEditMessage);
        Assert.Equal(started, (await LoadShowing(id)).StartTime);
    }

    [Fact]
    public async Task Us130_Ac5_Edit_is_allowed_when_the_only_bookings_are_cancelled()
    {
        var id = await SeedShowing(Winter, _salong1);
        await SeedBooking(id, _seats[0], BookingStatus.Cancelled, "AAAAAAAA");

        var response = await Edit(id, Summer);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Summer, (await LoadShowing(id)).StartTime);
    }

    [Fact]
    public async Task Us130_Ac5_A_cancelled_showing_cannot_be_edited()
    {
        var id = await SeedShowing(Winter, _salong1, ShowingStatus.Cancelled);

        var response = await Edit(id, Summer);

        await AssertProblem(response, HttpStatusCode.Conflict, "Cancelled showings cannot be edited.");
    }

    [Fact]
    public async Task Us130_Ac6_One_BookingCancelled_event_is_raised_per_cancelled_booking()
    {
        var id = await SeedShowing(Winter, _salong1);
        var first = await SeedBooking(id, _seats[0], BookingStatus.Confirmed, "AAAAAAAA");
        var second = await SeedBooking(id, _seats[1], BookingStatus.Confirmed, "BBBBBBBB");
        await SeedBooking(id, _seats[2], BookingStatus.Cancelled, "CCCCCCCC");

        await Send(HttpMethod.Post, $"{Url}/{id}/cancel");

        Assert.Equal(
            new[] { (first, "AAAAAAAA", id, "customer-1"), (second, "BBBBBBBB", id, "customer-1") },
            _events.Received.OrderBy(e => e.BookingId).Select(e => (e.BookingId, e.Reference, e.ShowingId, e.UserId)).ToArray());
    }

    [Fact]
    public async Task Us130_Ac6_A_failing_event_subscriber_does_not_undo_the_committed_cancellation()
    {
        var id = await SeedShowing(Winter, _salong1);
        await SeedBooking(id, _seats[0], BookingStatus.Confirmed, "AAAAAAAA");
        var throwing = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBookingEvents>();
            services.AddSingleton<IBookingEvents, ThrowingEvents>();
        }));

        var response = await Send(HttpMethod.Post, $"{Url}/{id}/cancel", factory: throwing);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ShowingStatus.Cancelled, (await LoadShowing(id)).Status);
        Assert.Equal(BookingStatus.Cancelled, (await LoadBookings()).Single().Status);
    }

    [Theory]
    [InlineData(null, null, 401)]
    [InlineData(Roles.Customer, null, 403)]
    [InlineData(Roles.Staff, null, 403)]
    [InlineData(Roles.Staff, Permissions.ManageMovies, 403)]
    [InlineData(Roles.Staff, Permissions.ManageShowings, 404)]
    [InlineData(Roles.Admin, null, 404)]
    public async Task Us130_Ac7_Edit_cancel_delete_and_read_of_an_unknown_showing_by_caller(string? role, string? permission, int expected)
    {
        var token = role is null ? null : Token(role, permission is null ? Array.Empty<string>() : new[] { permission });
        var body = new { MovieId = _movieId, AuditoriumId = _salong1, StartTime = Winter };

        var results = new[]
        {
            await Send(HttpMethod.Put, $"{Url}/9999", body, token),
            await Send(HttpMethod.Post, $"{Url}/9999/cancel", null, token),
            await Send(HttpMethod.Delete, $"{Url}/9999", null, token),
            await Send(HttpMethod.Get, $"{Url}/9999", null, token),
            await Send(HttpMethod.Get, $"{Url}?from=2000-01-01&to=2100-01-01", null, token)
        };

        Assert.Equal(new[] { expected, expected, expected, expected, expected == 404 ? 200 : expected },
            results.Select(r => (int)r.StatusCode).ToArray());
    }

    [Fact]
    public async Task Us130_Get_returns_the_showing_with_its_confirmed_booking_count()
    {
        var id = await SeedShowing(Winter, _salong1);
        await SeedBooking(id, _seats[0], BookingStatus.Confirmed, "AAAAAAAA");
        await SeedBooking(id, _seats[1], BookingStatus.Cancelled, "BBBBBBBB");

        var response = await Send(HttpMethod.Get, $"{Url}/{id}");

        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<ShowingDto>(Json))!.ActiveBookingCount);
    }

    [Fact]
    public async Task Us130_List_filters_by_period_and_auditorium_and_orders_by_start_time()
    {
        var later = await SeedShowing(Winter.AddHours(5), _salong1);
        var first = await SeedShowing(Winter, _salong1);
        await SeedShowing(Winter, _salong2);
        await SeedShowing(Summer, _salong1);
        await SeedBooking(first, _seats[0], BookingStatus.Confirmed, "AAAAAAAA");
        var from = Winter.AddHours(-1).ToString("o");
        var to = Winter.AddDays(1).ToString("o");

        var response = await Send(HttpMethod.Get, $"{Url}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&auditoriumId={_salong1}");

        var list = (await response.Content.ReadFromJsonAsync<ShowingDto[]>(Json))!;
        Assert.Equal(new[] { first, later }, list.Select(s => s.Id).ToArray());
        Assert.Equal(new[] { 1, 0 }, list.Select(s => s.ActiveBookingCount).ToArray());
    }

    [Theory]
    [InlineData("?to=2100-01-01", "from")]
    [InlineData("?from=2000-01-01", "to")]
    [InlineData("?from=2100-01-01&to=2000-01-01", "to")]
    public async Task Us130_List_with_a_missing_or_reversed_period_returns_400(string query, string field)
    {
        var response = await Send(HttpMethod.Get, Url + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))[field]);
    }

    private sealed class RecordingEvents : IBookingEvents
    {
        public List<BookingCancelled> Received { get; } = [];

        public Task BookingCancelledAsync(BookingCancelled e, CancellationToken ct = default)
        {
            lock (Received)
            {
                Received.Add(e);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEvents : IBookingEvents
    {
        public Task BookingCancelledAsync(BookingCancelled e, CancellationToken ct = default) =>
            throw new InvalidOperationException("subscriber failed");
    }

    private sealed class FailingOnSecondBookingService(AppDbContext db, IPriceCalculator prices, TimeProvider clock,
        IBookingEventPublisher publisher) : BookingService(db, prices, clock, publisher)
    {
        private int _calls;

        public override Task<Booking> CancelInternalAsync(Booking booking, CancellationToken ct = default) =>
            ++_calls == 2 ? throw new InvalidOperationException("cancel failed") : base.CancelInternalAsync(booking, ct);
    }
}
