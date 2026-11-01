using System.Net;
using System.Text;
using System.Text.Json;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Client.Services;

namespace NewtonsBiograf.Tests;

/// <summary>Movie management screens (US-125): the typed API client that maps HTTP results for the pages.</summary>
public class MovieClientTests
{
    private static readonly string[] Fields = ["Title", "Genre", "AgeRating", "LengthMinutes", "Description"];

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond();
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("Api", name);
            return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://api.test/") };
        }
    }

    private static (MovieApiClient Client, StubHandler Stub) ClientFor(HttpStatusCode status, string? json = null, string contentType = "application/json")
    {
        var stub = new StubHandler(() => new HttpResponseMessage(status)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, contentType)
        });
        return (new MovieApiClient(new StubFactory(stub)), stub);
    }

    private static readonly MovieRequest Alien = new()
    {
        Title = "Alien",
        Genre = Genre.SciFi,
        AgeRating = AgeRating.Age15,
        LengthMinutes = 117
    };

    [Fact]
    public async Task US125_Ac1_GetAllAsync_reads_the_movies_with_enum_names()
    {
        var (client, stub) = ClientFor(HttpStatusCode.OK,
            """[{"id":1,"title":"Alien","genre":"SciFi","ageRating":"AllAges","lengthMinutes":117,"description":null}]""");

        var result = await client.GetAllAsync();

        Assert.True(result.Success);
        var movie = Assert.Single(result.Movies!);
        Assert.Equal(new MovieDto(1, "Alien", Genre.SciFi, AgeRating.AllAges, 117, null), movie);
        Assert.Equal(HttpMethod.Get, stub.Method);
        Assert.Equal("/api/movies", stub.Path);
    }

    [Fact]
    public async Task US125_Ac1_CreateAsync_posts_and_UpdateAsync_puts_to_the_row_id_with_enums_as_names()
    {
        var (client, stub) = ClientFor(HttpStatusCode.Created);

        Assert.True((await client.CreateAsync(Alien)).Success);
        Assert.Equal(HttpMethod.Post, stub.Method);
        Assert.Equal("/api/movies", stub.Path);
        using (var body = JsonDocument.Parse(stub.Body!))
        {
            Assert.Equal("Alien", body.RootElement.GetProperty("title").GetString());
            Assert.Equal("SciFi", body.RootElement.GetProperty("genre").GetString());
            Assert.Equal("Age15", body.RootElement.GetProperty("ageRating").GetString());
            Assert.Equal(117, body.RootElement.GetProperty("lengthMinutes").GetInt32());
        }

        var (updateClient, updateStub) = ClientFor(HttpStatusCode.NoContent);
        Assert.True((await updateClient.UpdateAsync(7, Alien)).Success);
        Assert.Equal(HttpMethod.Put, updateStub.Method);
        Assert.Equal("/api/movies/7", updateStub.Path);
    }

    [Fact]
    public async Task US125_Ac2_Validation_errors_map_to_the_matching_fields_case_insensitively()
    {
        var (client, _) = ClientFor(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"Title":["Title is required"],"lengthMinutes":["Length must be a whole number between 1 and 400"]}}""",
            "application/problem+json");

        var result = await client.CreateAsync(Alien with { Title = "", LengthMinutes = 0 });

        Assert.False(result.Success);
        Assert.False(result.NotFound);
        Assert.Equal("Title is required", result.Problem!.Field("Title"));
        Assert.Equal("Length must be a whole number between 1 and 400", result.Problem.Field("LengthMinutes"));
        Assert.Null(result.Problem.Field("Genre"));
        Assert.Null(result.Problem.General(Fields));
    }

    [Fact]
    public async Task US125_Ac2_An_error_key_that_is_not_a_form_field_goes_to_the_general_message()
    {
        var (client, _) = ClientFor(HttpStatusCode.BadRequest,
            """{"errors":{"":["The request could not be read."],"Title":["Title is required"]}}""",
            "application/problem+json");

        var result = await client.CreateAsync(Alien);

        Assert.Equal("The request could not be read.", result.Problem!.General(Fields));
    }

    [Fact]
    public async Task US125_Ac3_A_409_returns_the_API_detail_and_the_call_is_not_a_success()
    {
        var (client, stub) = ClientFor(HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"Movie cannot be deleted because it has future showings."}""",
            "application/problem+json");

        var result = await client.DeleteAsync(3);

        Assert.False(result.Success);
        Assert.False(result.NotFound);
        Assert.Equal("Movie cannot be deleted because it has future showings.", result.Problem!.General());
        Assert.Equal(HttpMethod.Delete, stub.Method);
        Assert.Equal("/api/movies/3", stub.Path);
    }

    [Fact]
    public async Task US125_Ac3_A_204_delete_succeeds_and_a_404_is_flagged_so_the_page_reloads()
    {
        var (deleted, _) = ClientFor(HttpStatusCode.NoContent);
        var (gone, _) = ClientFor(HttpStatusCode.NotFound, """{"title":"Not Found","status":404}""", "application/problem+json");

        Assert.True((await deleted.DeleteAsync(3)).Success);
        var result = await gone.DeleteAsync(3);
        Assert.False(result.Success);
        Assert.True(result.NotFound);
        Assert.Equal(MovieApiClient.NotFoundMessage, result.Problem!.General());
    }

    [Fact]
    public async Task US125_Ac1_A_403_shows_the_permission_message_and_a_dead_connection_the_unreachable_message()
    {
        var (forbidden, _) = ClientFor(HttpStatusCode.Forbidden, """{"title":"You do not have access to this resource"}""", "application/problem+json");
        var offline = new MovieApiClient(new StubFactory(new ThrowingHandler()));

        Assert.Equal("You do not have permission to manage movies.", (await forbidden.GetAllAsync()).Problem!.General());
        var result = await offline.GetAllAsync();
        Assert.False(result.Success);
        Assert.Same(ApiProblem.Unreachable, result.Problem);
    }

    [Fact]
    public async Task US125_Ac1_An_unreadable_success_body_is_reported_instead_of_throwing()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK, "<html></html>", "text/html");

        var result = await client.GetAllAsync();

        Assert.False(result.Success);
        Assert.Same(ApiProblem.Unexpected, result.Problem);
    }
}
