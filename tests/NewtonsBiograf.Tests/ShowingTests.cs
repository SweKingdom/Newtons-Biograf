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
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

public sealed class ShowingTests : IAsyncLifetime
{
    private const string Url = "/api/showings";
    private const int MovieLength = 175;
    private const int Slot = MovieLength + 15;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StrictEnumConverterFactory() }
    };

    /// <summary>15 January next year, 17:00 UTC = 18:00 in Stockholm (winter time); always in the future.</summary>
    private static DateTime Winter => new(DateTime.UtcNow.Year + 1, 1, 15, 17, 0, 0, DateTimeKind.Utc);

    /// <summary>15 July next year, 15:00 UTC = 17:00 in Stockholm (summer time); always in the future.</summary>
    private static DateTime Summer => new(DateTime.UtcNow.Year + 1, 7, 15, 15, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplicationFactory<Program> _factory = null!;
    private int _movieId;
    private int _salong1;
    private int _salong2;

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
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var movie = new Movie { Title = "Dune", Genre = Genre.SciFi, AgeRating = AgeRating.Age11, LengthMinutes = MovieLength };
        var a1 = new Auditorium { Name = "Salong 1", Rows = 1, SeatsPerRow = 1 };
        var a2 = new Auditorium { Name = "Salong 2", Rows = 1, SeatsPerRow = 1 };
        db.AddRange(movie, a1, a2);
        await db.SaveChangesAsync();
        (_movieId, _salong1, _salong2) = (movie.Id, a1.Id, a2.Id);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private string Token(string role, params string[] permissions) =>
        _factory.Services.GetRequiredService<TokenService>().Create(
            new ApplicationUser { Id = "caller-1", Email = "caller@example.com", DisplayName = "Caller" }, role, permissions).Token;

    private async Task<HttpResponseMessage> Post(object body, string? token = "admin")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token == "admin" ? Token(Roles.Admin) : token);
        }
        using var client = _factory.CreateClient();
        return await client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Create(DateTime start, int? auditoriumId = null, string? token = "admin") =>
        Post(new { MovieId = _movieId, AuditoriumId = auditoriumId ?? _salong1, StartTime = start }, token);

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> ShowingCount() => WithDb(db => db.Showings.CountAsync());

    /// <summary>Seeds a showing of the movie lasting <paramref name="minutes"/> (no cleaning time) so the end is explicit.</summary>
    private Task SeedShowing(DateTime start, int auditoriumId, ShowingStatus status = ShowingStatus.Scheduled, int minutes = MovieLength) =>
        WithDb(async db =>
        {
            db.Showings.Add(new Showing
            {
                MovieId = _movieId, AuditoriumId = auditoriumId, StartTime = start, EndTime = start.AddMinutes(minutes), Status = status
            });
            return await db.SaveChangesAsync();
        });

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        return new Dictionary<string, string[]>(problem!.Errors, StringComparer.OrdinalIgnoreCase);
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

        using var client = _factory.CreateClient();
        using var get = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location);
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(Roles.Admin));
        var read = await client.SendAsync(get);
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

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("Overlaps with 'Dune' 18:00–20:55 in Salong 1", problem.Detail);
        Assert.Equal(1, await ShowingCount());
    }

    [Fact]
    public async Task Ac3_The_conflict_detail_uses_the_display_time_zone_with_daylight_saving_time()
    {
        await SeedShowing(Summer, _salong1);

        var response = await Create(Summer.AddMinutes(30));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Overlaps with 'Dune' 17:00–19:55 in Salong 1",
            (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
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
}
