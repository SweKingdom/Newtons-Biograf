using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Client.Pages;
using NewtonsBiograf.Client.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

/// <summary>US-133: the public browsing screens' typed client, time helpers, book links and page routes.</summary>
public class PublicClientTests
{
    private sealed class UriHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            return Task.FromResult(respond());
        }
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private static (PublicApiClient Client, UriHandler Handler) ClientFor(HttpStatusCode status, string? json = null, string contentType = "application/json")
    {
        var handler = new UriHandler(() => new HttpResponseMessage(status)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, contentType)
        });
        return (new PublicApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }), handler);
    }

    private static ShowingListItem Showing(DateTime startUtc) =>
        new(1, 3, "Dune", "Sci-Fi", "11", 175, 2, "Salong 2", startUtc, startUtc.AddMinutes(190), 5);

    [Fact]
    public async Task Ac2_GetShowingsAsync_sends_the_filters_escaped_and_leaves_out_empty_ones()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, "[]");

        await client.GetShowingsAsync(new ShowingFilterModel
        {
            Genre = "Sci-Fi", Date = new DateOnly(2030, 1, 5), AuditoriumId = 2, Title = "  åsa & co "
        });
        Assert.Equal("/api/public/showings", handler.Uri!.AbsolutePath);
        Assert.Equal("?genre=Sci-Fi&date=2030-01-05&auditoriumId=2&title=%C3%A5sa%20%26%20co", handler.Uri.Query);

        await client.GetShowingsAsync(new ShowingFilterModel { Genre = "", Title = "   " });
        Assert.Equal("", handler.Uri.Query);

        await client.GetShowingsAsync();
        Assert.Equal("", handler.Uri.Query);
    }

    [Fact]
    public async Task Ac1_GetShowingsAsync_reads_the_showings_and_genres_and_auditoriums_are_read_from_their_routes()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK,
            """[{"id":1,"movieId":3,"movie":"Dune","genre":"Sci-Fi","ageRating":"11","lengthMinutes":175,"auditoriumId":2,"auditorium":"Salong 2","start":"2030-01-15T17:00:00Z","end":"2030-01-15T20:10:00Z","freeSeats":5}]""");

        var result = await client.GetShowingsAsync();

        Assert.True(result.Success);
        Assert.Equal(Showing(new DateTime(2030, 1, 15, 17, 0, 0, DateTimeKind.Utc)), Assert.Single(result.Value!));

        var (genres, genresHandler) = ClientFor(HttpStatusCode.OK, """["Comedy","Sci-Fi"]""");
        Assert.Equal(["Comedy", "Sci-Fi"], (await genres.GetGenresAsync()).Value);
        Assert.Equal("/api/public/genres", genresHandler.Uri!.AbsolutePath);

        var (auditoriums, auditoriumsHandler) = ClientFor(HttpStatusCode.OK, """[{"id":2,"name":"Salong 2"}]""");
        Assert.Equal([new AuditoriumOption(2, "Salong 2")], (await auditoriums.GetAuditoriumsAsync()).Value);
        Assert.Equal("/api/public/auditoriums", auditoriumsHandler.Uri!.AbsolutePath);
        Assert.Equal("/api/public/showings", handler.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task Ac3_GetMovieAsync_reads_the_movie_and_a_404_is_flagged_as_not_found()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK,
            """{"id":3,"title":"Dune","description":"Sand.","lengthMinutes":175,"genre":"Sci-Fi","ageRating":"11","showings":[]}""");
        var (missing, _) = ClientFor(HttpStatusCode.NotFound, """{"title":"Not Found","status":404}""", "application/problem+json");

        var found = await client.GetMovieAsync(3);
        var gone = await missing.GetMovieAsync(9999);

        Assert.Equal("/api/public/movies/3", handler.Uri!.AbsolutePath);
        Assert.Equal(new MovieDetailDto(3, "Dune", "Sand.", 175, "Sci-Fi", "11", []), found.Value with { Showings = [] });
        Assert.True(found.Success);
        Assert.True(gone.NotFound);
        Assert.False(gone.Success);
        Assert.Null(gone.Error);
    }

    [Fact]
    public async Task Ac2_A_failed_dead_or_unreadable_call_gives_a_plain_error_instead_of_throwing()
    {
        var (broken, _) = ClientFor(HttpStatusCode.BadGateway, "<html></html>", "text/html");
        var (garbled, _) = ClientFor(HttpStatusCode.OK, "<html></html>", "text/html");
        var offline = new PublicApiClient(new HttpClient(new OfflineHandler()) { BaseAddress = new Uri("http://api.test/") });

        var failed = await broken.GetShowingsAsync();
        var unreadable = await garbled.GetShowingsAsync();
        var dead = await offline.GetShowingsAsync();

        Assert.False(failed.Success);
        Assert.Equal("Something went wrong. Please try again.", failed.Error);
        Assert.False(unreadable.Success);
        Assert.Equal("Something went wrong. Please try again.", unreadable.Error);
        Assert.False(dead.Success);
        Assert.Equal(ApiProblem.Unreachable.General(), dead.Error);
    }

    [Fact]
    public void Ac1_Showings_are_grouped_by_the_calendar_day_in_the_display_time_zone()
    {
        var stockholm = DisplayTime.Resolve("Europe/Stockholm");
        var lateEvening = Showing(new DateTime(2030, 1, 15, 22, 0, 0, DateTimeKind.Utc)); // 23:00 on the 15th
        var afterMidnight = Showing(new DateTime(2030, 1, 15, 23, 30, 0, DateTimeKind.Utc)); // 00:30 on the 16th
        var noon = Showing(new DateTime(2030, 1, 16, 11, 0, 0, DateTimeKind.Utc));

        var days = DisplayTime.GroupByDay([lateEvening, afterMidnight, noon], stockholm).ToList();

        Assert.Equal([new DateTime(2030, 1, 15), new DateTime(2030, 1, 16)], days.Select(d => d.Key));
        Assert.Equal([lateEvening], days[0]);
        Assert.Equal([afterMidnight, noon], days[1]);
        Assert.Equal(new DateTime(2030, 1, 16, 0, 30, 0), DisplayTime.ToZone(afterMidnight.Start, stockholm));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void Ac1_A_missing_or_unknown_time_zone_falls_back_to_the_browsers_zone(string? id)
    {
        Assert.Equal(TimeZoneInfo.Local, DisplayTime.Resolve(id));
    }

    [Fact]
    public void Ac4_Book_goes_to_the_seat_map_when_signed_in_and_to_login_with_a_return_address_when_anonymous()
    {
        Assert.Equal("/showings/7/book", AuthRedirect.BookUrl(7, signedIn: true));

        var anonymous = AuthRedirect.BookUrl(7, signedIn: false);

        Assert.Equal("login?returnUrl=%2Fshowings%2F7%2Fbook", anonymous);
        Assert.Equal("/showings/7/book", AuthRedirect.SafeReturnUrl(Uri.UnescapeDataString("%2Fshowings%2F7%2Fbook")));
    }

    [Fact]
    public void Ac1_The_home_and_movie_pages_have_their_routes_and_need_no_login()
    {
        Assert.Equal("/", typeof(Home).GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal("/movies/{Id:int}", typeof(MovieDetail).GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Null(typeof(Home).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(MovieDetail).GetCustomAttribute<AuthorizeAttribute>());
    }
}
