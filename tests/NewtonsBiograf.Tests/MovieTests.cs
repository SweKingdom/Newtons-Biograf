using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

public sealed class MovieTests : IAsyncLifetime
{
    private const string Url = "/api/movies";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private string Token(string role, params string[] permissions) =>
        _factory.Services.GetRequiredService<TokenService>().Create(
            new ApplicationUser { Id = "caller-1", Email = "caller@example.com", DisplayName = "Caller" }, role, permissions).Token;

    private string StaffToken => Token(Roles.Staff, Permissions.ManageMovies);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
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

    /// <summary>A valid request body; genre and age rating are objects so tests can send numbers or unknown strings.</summary>
    private static object Body(string? title = "Dune", object? genre = null, object? ageRating = null,
        int length = 155, string? description = "Spice.") =>
        new { Title = title, Genre = genre ?? "SciFi", AgeRating = ageRating ?? "Age11", LengthMinutes = length, Description = description };

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        return new Dictionary<string, string[]>(problem!.Errors, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> MovieCount() => WithDb(db => db.Movies.CountAsync());

    private Task<int> SeedMovie(string title = "Existing") => WithDb(async db =>
    {
        var movie = new Movie { Title = title, Genre = Genre.Drama, AgeRating = AgeRating.Age15, LengthMinutes = 100 };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    });

    private Task SeedShowing(int movieId, TimeSpan startsIn, ShowingStatus status) => WithDb(async db =>
    {
        var auditorium = await db.Auditoriums.FirstOrDefaultAsync() ?? new Auditorium { Name = "Salong 1", Rows = 1, SeatsPerRow = 1 };
        var start = DateTime.UtcNow + startsIn;
        db.Showings.Add(new Showing
        {
            MovieId = movieId, Auditorium = auditorium, StartTime = start, EndTime = start.AddHours(2), Status = status
        });
        return await db.SaveChangesAsync();
    });

    [Fact]
    public async Task Ac1_Create_returns_201_with_the_movie_and_stored_values_match_input()
    {
        var response = await Send(HttpMethod.Post, Url, StaffToken, Body(title: "  Dune  ", description: "Spice."));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<MovieDto>(Json))!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal(new MovieDto(created.Id, "Dune", Genre.SciFi, AgeRating.Age11, 155, "Spice."), created);

        var fetched = await Send(HttpMethod.Get, $"{Url}/{created.Id}");
        Assert.Equal(created, await fetched.Content.ReadFromJsonAsync<MovieDto>(Json));
        var stored = await WithDb(db => db.Movies.SingleAsync());
        Assert.Equal("Dune", stored.Title);
        Assert.Equal(Genre.SciFi, stored.Genre);
        Assert.Equal(AgeRating.Age11, stored.AgeRating);
        Assert.Equal(155, stored.LengthMinutes);
        Assert.Equal("Spice.", stored.Description);
    }

    [Fact]
    public async Task Ac1_Description_is_optional_and_list_is_ordered_by_title()
    {
        await Send(HttpMethod.Post, Url, StaffToken, Body(title: "Zodiac", description: null));
        await Send(HttpMethod.Post, Url, StaffToken, Body(title: "Alien", description: null));

        var list = (await (await Send(HttpMethod.Get, Url)).Content.ReadFromJsonAsync<MovieDto[]>(Json))!;

        Assert.Equal(["Alien", "Zodiac"], list.Select(m => m.Title));
        Assert.All(list, m => Assert.Null(m.Description));
    }

    [Fact]
    public async Task Ac2_Invalid_fields_return_400_keyed_by_field_and_save_nothing()
    {
        var response = await Send(HttpMethod.Post, Url, StaffToken,
            Body(title: "", length: 0, description: new string('x', 2001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await Errors(response);
        Assert.Equal("Title is required", Assert.Single(errors["Title"]));
        Assert.Equal("Length must be a whole number between 1 and 400", Assert.Single(errors["LengthMinutes"]));
        Assert.Equal("Description must be at most 2000 characters", Assert.Single(errors["Description"]));
        Assert.Equal(0, await MovieCount());
    }

    [Theory]
    [InlineData("   ", 100, "Title is required", "Title")]
    [InlineData("T", 401, "Length must be a whole number between 1 and 400", "LengthMinutes")]
    [InlineData("T", -5, "Length must be a whole number between 1 and 400", "LengthMinutes")]
    public async Task Ac2_Whitespace_title_and_out_of_range_length_are_rejected(string title, int length, string message, string field)
    {
        var response = await Send(HttpMethod.Post, Url, StaffToken, Body(title: title, length: length));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, Assert.Single((await Errors(response))[field]));
        Assert.Equal(0, await MovieCount());
    }

    [Fact]
    public async Task Ac2_Title_over_200_characters_is_rejected_and_200_is_accepted()
    {
        var tooLong = await Send(HttpMethod.Post, Url, StaffToken, Body(title: new string('t', 201)));
        var max = await Send(HttpMethod.Post, Url, StaffToken, Body(title: new string('t', 200)));

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("Title must be at most 200 characters", Assert.Single((await Errors(tooLong))["Title"]));
        Assert.Equal(HttpStatusCode.Created, max.StatusCode);
    }

    [Theory]
    [InlineData("Genre", "Western")]
    [InlineData("Genre", 1)]
    [InlineData("Genre", "1")]
    [InlineData("Genre", 99)]
    [InlineData("Genre", null)]
    [InlineData("AgeRating", "Age18")]
    [InlineData("AgeRating", 2)]
    [InlineData("AgeRating", null)]
    public async Task Ac2_Unknown_numeric_or_null_genre_and_age_rating_return_400_keyed_by_field(string field, object? value)
    {
        var body = new Dictionary<string, object?>
        {
            ["Title"] = "Dune", ["Genre"] = "SciFi", ["AgeRating"] = "Age11", ["LengthMinutes"] = 100
        };
        body[field] = value;

        var response = await Send(HttpMethod.Post, Url, StaffToken, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))[field]);
        Assert.Equal(0, await MovieCount());
    }

    [Fact]
    public async Task Ac2_Missing_genre_and_age_rating_say_they_are_required()
    {
        var response = await Send(HttpMethod.Post, Url, StaffToken, new { Title = "Dune", LengthMinutes = 100 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await Errors(response);
        Assert.Equal("Genre is required", Assert.Single(errors["Genre"]));
        Assert.Equal("Age rating is required", Assert.Single(errors["AgeRating"]));
    }

    [Fact]
    public async Task Ac3_PUT_valid_returns_204_and_persists_and_the_route_id_wins()
    {
        var id = await SeedMovie();
        var other = await SeedMovie("Other");

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", StaffToken,
            new { Id = other, Title = " Heat ", Genre = "Thriller", AgeRating = "AllAges", LengthMinutes = 170, Description = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var movies = await WithDb(db => db.Movies.OrderBy(m => m.Id).ToListAsync());
        Assert.Equal("Heat", movies[0].Title);
        Assert.Equal(Genre.Thriller, movies[0].Genre);
        Assert.Equal(AgeRating.AllAges, movies[0].AgeRating);
        Assert.Equal(170, movies[0].LengthMinutes);
        Assert.Null(movies[0].Description);
        Assert.Equal("Other", movies[1].Title);
    }

    [Fact]
    public async Task Ac3_PUT_invalid_returns_400_and_changes_nothing()
    {
        var id = await SeedMovie();

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", StaffToken, Body(title: "", length: 401));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await Errors(response);
        Assert.NotEmpty(errors["Title"]);
        Assert.NotEmpty(errors["LengthMinutes"]);
        Assert.Equal("Existing", (await WithDb(db => db.Movies.SingleAsync())).Title);
    }

    [Fact]
    public async Task Ac3_PUT_unknown_id_returns_404()
    {
        var response = await Send(HttpMethod.Put, $"{Url}/9999", StaffToken, Body());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ac3_GET_unknown_id_returns_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"{Url}/9999")).StatusCode);
    }

    [Fact]
    public async Task Ac4_Delete_with_a_future_scheduled_showing_returns_409_and_keeps_the_movie()
    {
        var id = await SeedMovie();
        await SeedShowing(id, TimeSpan.FromDays(1), ShowingStatus.Scheduled);

        var response = await Send(HttpMethod.Delete, $"{Url}/{id}", StaffToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("Movie cannot be deleted because it has future showings.", problem.Detail);
        Assert.Equal(1, await MovieCount());
    }

    [Fact]
    public async Task Ac5_Delete_movie_without_showings_returns_204_and_it_is_gone()
    {
        var id = await SeedMovie();

        var response = await Send(HttpMethod.Delete, $"{Url}/{id}", StaffToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"{Url}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Ac5_Delete_unknown_id_returns_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, $"{Url}/9999", StaffToken)).StatusCode);
    }

    [Theory]
    [InlineData(-1, ShowingStatus.Scheduled)]
    [InlineData(1, ShowingStatus.Cancelled)]
    public async Task Ac5_Movie_with_only_past_or_cancelled_showings_is_kept_with_409_because_history_is_never_cascaded(
        int days, ShowingStatus status)
    {
        var id = await SeedMovie();
        await SeedShowing(id, TimeSpan.FromDays(days), status);

        var response = await Send(HttpMethod.Delete, $"{Url}/{id}", StaffToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Movie cannot be deleted because it has showings.",
            (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
        Assert.Equal(1, await MovieCount());
        Assert.Equal(1, await WithDb(db => db.Showings.CountAsync()));
    }

    [Theory]
    [InlineData(null, null, 401)]
    [InlineData(Roles.Customer, null, 403)]
    [InlineData(Roles.Staff, Permissions.ManageShowings, 403)]
    [InlineData(Roles.Staff, Permissions.ManageMovies, 0)]
    [InlineData(Roles.Admin, null, 0)]
    public async Task Ac6_Write_endpoints_by_caller(string? role, string? permission, int expectedDenied)
    {
        var id = await SeedMovie();
        var token = role is null ? null : Token(role, permission is null ? Array.Empty<string>() : new[] { permission });

        var post = await Send(HttpMethod.Post, Url, token, Body());
        var put = await Send(HttpMethod.Put, $"{Url}/{id}", token, Body(title: "Changed"));
        var delete = await Send(HttpMethod.Delete, $"{Url}/{id}", token);

        if (expectedDenied == 0)
        {
            Assert.Equal(HttpStatusCode.Created, post.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }
        else
        {
            Assert.All(new[] { post, put, delete }, r => Assert.Equal(expectedDenied, (int)r.StatusCode));
            Assert.Equal("Existing", (await WithDb(db => db.Movies.SingleAsync())).Title);
        }
    }

    [Fact]
    public async Task Ac6_Anonymous_GET_list_and_detail_return_200()
    {
        var id = await SeedMovie();

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, $"{Url}/{id}")).StatusCode);
    }

    [Fact]
    public void Enum_display_names_are_shown_to_users_while_wire_values_stay_enum_names()
    {
        Assert.Equal("Sci-Fi", Genre.SciFi.ToDisplayName());
        Assert.Equal("Drama", Genre.Drama.ToDisplayName());
        Assert.Equal("All ages", AgeRating.AllAges.ToDisplayName());
        Assert.Equal("7", AgeRating.Age7.ToDisplayName());
        Assert.Equal("\"SciFi\"", JsonSerializer.Serialize(Genre.SciFi, Json));
    }
}
