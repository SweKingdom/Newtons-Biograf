using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Client.Pages.Staff;
using NewtonsBiograf.Client.Services;
using NewtonsBiograf.Shared;
using static NewtonsBiograf.Tests.MovieClientTests;

namespace NewtonsBiograf.Tests;

/// <summary>Showing management screens (US-131): the typed API client, time helpers, settings endpoint and page access.</summary>
public class ShowingClientTests
{
    private static readonly string[] Fields = ["MovieId", "AuditoriumId", "StartTime"];
    private static readonly DateTime Start = new(2030, 1, 15, 17, 0, 0, DateTimeKind.Utc);
    private static readonly ShowingRequest Request = new() { MovieId = 4, AuditoriumId = 2, StartTime = Start };

    private sealed class UriHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        }
    }

    private static (ShowingApiClient Client, StubHandler Stub) ClientFor(HttpStatusCode status, string? json = null, string contentType = "application/json")
    {
        var stub = new StubHandler(() => new HttpResponseMessage(status)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, contentType)
        });
        return (new ShowingApiClient(new StubFactory(stub)), stub);
    }

    [Fact]
    public async Task US131_Ac1_ListAsync_sends_the_range_as_UTC_with_Z_and_the_auditorium_and_reads_the_showings()
    {
        var uriHandler = new UriHandler();
        var client = new ShowingApiClient(new StubFactory(uriHandler));

        await client.ListAsync(Start, Start.AddDays(1), 2);

        Assert.Equal("/api/showings", uriHandler.Uri!.AbsolutePath);
        Assert.Equal("?from=2030-01-15T17:00:00Z&to=2030-01-16T17:00:00Z&auditoriumId=2",
            Uri.UnescapeDataString(uriHandler.Uri.Query));

        await client.ListAsync(Start, Start.AddDays(1));
        Assert.DoesNotContain("auditoriumId", uriHandler.Uri.Query);

        var (reading, _) = ClientFor(HttpStatusCode.OK,
            """[{"id":9,"movieId":4,"movieTitle":"Dune","auditoriumId":2,"auditoriumName":"Salong 2","startTime":"2030-01-15T17:00:00Z","endTime":"2030-01-15T20:10:00Z","status":"Scheduled","activeBookingCount":3}]""");
        var result = await reading.ListAsync(Start, Start.AddDays(1));
        var showing = Assert.Single(result.Showings!);
        Assert.Equal(new ShowingDto(9, 4, "Dune", 2, "Salong 2", Start, Start.AddMinutes(190), ShowingStatus.Scheduled, 3), showing);
    }

    [Fact]
    public async Task US131_Ac1_GetSettingsAsync_reads_the_cleaning_minutes()
    {
        var (client, stub) = ClientFor(HttpStatusCode.OK, """{"cleaningMinutes":15}""");

        var result = await client.GetSettingsAsync();

        Assert.Equal(new ShowingSettingsDto(15), result.Settings);
        Assert.Equal("/api/showings/settings", stub.Path);
    }

    [Fact]
    public async Task US131_Ac1_CreateAsync_posts_ids_and_the_start_as_ISO_UTC_with_Z()
    {
        var (client, stub) = ClientFor(HttpStatusCode.Created);

        var result = await client.CreateAsync(Request with
        {
            StartTime = DateTime.SpecifyKind(new DateTime(2030, 1, 15, 17, 0, 0), DateTimeKind.Utc)
        });

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, stub.Method);
        Assert.Equal("/api/showings", stub.Path);
        using var body = JsonDocument.Parse(stub.Body!);
        Assert.Equal(4, body.RootElement.GetProperty("movieId").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("auditoriumId").GetInt32());
        Assert.Equal("2030-01-15T17:00:00Z", body.RootElement.GetProperty("startTime").GetString());
    }

    [Fact]
    public void US131_Ac1_Local_entry_converts_to_UTC_and_the_end_preview_adds_film_length_and_cleaning()
    {
        var utc = ShowingTimes.ToUtc(new DateOnly(2030, 1, 15), new TimeOnly(18, 0));

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal("2030-01-15T18:00:00", utc.ToLocalTime().ToString("s"));
        Assert.Equal(Start.AddMinutes(190), ShowingTimes.End(Start, 175, 15));
    }

    [Fact]
    public async Task US131_Ac2_A_400_maps_StartTime_to_the_field_and_a_409_overlap_detail_is_the_general_message()
    {
        var (invalid, _) = ClientFor(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"StartTime":["StartTime must be in the future"]}}""",
            "application/problem+json");
        var (overlap, _) = ClientFor(HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"Overlaps with 'Dune' 18:00–21:10 in Salong 1"}""",
            "application/problem+json");

        var badStart = await invalid.CreateAsync(Request);
        var clash = await overlap.CreateAsync(Request);

        Assert.False(badStart.Success);
        Assert.Equal("StartTime must be in the future", badStart.Problem!.Field("StartTime"));
        Assert.Null(badStart.Problem.General(Fields));
        Assert.False(clash.Success);
        Assert.Equal("Overlaps with 'Dune' 18:00–21:10 in Salong 1", clash.Problem!.General(Fields));
        Assert.Null(clash.Problem.Field("StartTime"));
    }

    [Fact]
    public async Task US131_Ac3_CancelAsync_posts_to_the_cancel_route_and_a_404_is_flagged()
    {
        var (client, stub) = ClientFor(HttpStatusCode.NoContent);
        var (gone, _) = ClientFor(HttpStatusCode.NotFound, """{"title":"Not Found","status":404}""", "application/problem+json");

        var result = await client.CancelAsync(9);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, stub.Method);
        Assert.Equal("/api/showings/9/cancel", stub.Path);
        var missing = await gone.CancelAsync(9);
        Assert.True(missing.NotFound);
        Assert.Equal(ShowingApiClient.NotFoundMessage, missing.Problem!.General());
    }

    [Fact]
    public async Task US131_Ac4_UpdateAsync_puts_and_a_409_from_edit_or_delete_returns_the_API_message()
    {
        var (updated, updateStub) = ClientFor(HttpStatusCode.NoContent);
        var (blockedEdit, _) = ClientFor(HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"Showing has bookings or has started and cannot be edited. Cancel it and create a new one."}""",
            "application/problem+json");
        var (blockedDelete, deleteStub) = ClientFor(HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"Showing has bookings and can only be cancelled."}""",
            "application/problem+json");

        Assert.True((await updated.UpdateAsync(9, Request)).Success);
        Assert.Equal(HttpMethod.Put, updateStub.Method);
        Assert.Equal("/api/showings/9", updateStub.Path);
        Assert.Equal("Showing has bookings or has started and cannot be edited. Cancel it and create a new one.",
            (await blockedEdit.UpdateAsync(9, Request)).Problem!.General());
        var delete = await blockedDelete.DeleteAsync(9);
        Assert.False(delete.Success);
        Assert.Equal("Showing has bookings and can only be cancelled.", delete.Problem!.General());
        Assert.Equal(HttpMethod.Delete, deleteStub.Method);
        Assert.Equal("/api/showings/9", deleteStub.Path);
    }

    [Fact]
    public async Task US131_Ac5_A_403_shows_the_permission_message_and_a_dead_connection_the_unreachable_message()
    {
        var (forbidden, _) = ClientFor(HttpStatusCode.Forbidden, """{"title":"You do not have access to this resource"}""", "application/problem+json");
        var offline = new ShowingApiClient(new StubFactory(new ThrowingHandler()));

        Assert.Equal(ShowingApiClient.ForbiddenMessage, (await forbidden.ListAsync(Start, Start.AddDays(1))).Problem!.General());
        Assert.Same(ApiProblem.Unreachable, (await offline.ListAsync(Start, Start.AddDays(1))).Problem);
    }

    [Fact]
    public void US131_Ac5_The_showings_page_requires_the_ManageShowings_policy()
    {
        var authorize = typeof(Showings).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(Permissions.ManageShowings, authorize?.Policy);
    }

    [Fact]
    public async Task US131_Ac1_The_settings_endpoint_is_anonymous_and_the_list_still_needs_a_token()
    {
        using var factory = new WebApplicationFactory<Program>();
        var http = factory.CreateClient();

        var settings = await http.GetAsync("/api/showings/settings");
        var list = await http.GetAsync("/api/showings?from=2030-01-15&to=2030-01-16");

        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal(new ShowingSettingsDto(15), await settings.Content.ReadFromJsonAsync<ShowingSettingsDto>());
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }
}
