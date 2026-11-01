using System.Net;
using System.Text;
using System.Text.Json;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Client.Services;
using NewtonsBiograf.Shared;
using static NewtonsBiograf.Tests.MovieClientTests;

namespace NewtonsBiograf.Tests;

/// <summary>Auditorium management screens (US-128): the typed API client that maps HTTP results for the pages.</summary>
public class AuditoriumClientTests
{
    private static readonly string[] Fields = ["Name", "Rows", "SeatsPerRow"];

    private static (AuditoriumApiClient Client, StubHandler Stub) ClientFor(HttpStatusCode status, string? json = null, string contentType = "application/json")
    {
        var stub = new StubHandler(() => new HttpResponseMessage(status)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, contentType)
        });
        return (new AuditoriumApiClient(new StubFactory(stub)), stub);
    }

    private static readonly AuditoriumRequest Sal4 = new() { Name = "Sal 4", Rows = 5, SeatsPerRow = 8 };

    [Fact]
    public async Task US128_Ac1_ListAsync_and_GetAsync_read_the_auditoriums_with_capacity_and_vip_rows()
    {
        var (client, stub) = ClientFor(HttpStatusCode.OK,
            """[{"id":1,"name":"Sal 4","rows":5,"seatsPerRow":8,"capacity":40,"vipRows":[2]}]""");

        var list = await client.ListAsync();

        Assert.True(list.Success);
        var auditorium = Assert.Single(list.Auditoriums!);
        Assert.Equal("Sal 4", auditorium.Name);
        Assert.Equal(40, auditorium.Capacity);
        Assert.Equal([2], auditorium.VipRows);
        Assert.Equal("/api/auditoriums", stub.Path);

        var (getClient, getStub) = ClientFor(HttpStatusCode.OK,
            """{"id":3,"name":"Sal 4","rows":5,"seatsPerRow":8,"capacity":40,"vipRows":[]}""");
        var get = await getClient.GetAsync(3);
        Assert.Equal("Sal 4", get.Auditorium!.Name);
        Assert.Equal("/api/auditoriums/3", getStub.Path);
    }

    [Fact]
    public async Task US128_Ac1_CreateAsync_posts_and_UpdateAsync_puts_the_name_rows_and_seats_per_row()
    {
        var (client, stub) = ClientFor(HttpStatusCode.Created);

        Assert.True((await client.CreateAsync(Sal4)).Success);
        Assert.Equal(HttpMethod.Post, stub.Method);
        Assert.Equal("/api/auditoriums", stub.Path);
        using (var body = JsonDocument.Parse(stub.Body!))
        {
            Assert.Equal("Sal 4", body.RootElement.GetProperty("name").GetString());
            Assert.Equal(5, body.RootElement.GetProperty("rows").GetInt32());
            Assert.Equal(8, body.RootElement.GetProperty("seatsPerRow").GetInt32());
        }

        var (updateClient, updateStub) = ClientFor(HttpStatusCode.NoContent);
        Assert.True((await updateClient.UpdateAsync(7, Sal4)).Success);
        Assert.Equal(HttpMethod.Put, updateStub.Method);
        Assert.Equal("/api/auditoriums/7", updateStub.Path);
    }

    [Fact]
    public async Task US128_Ac2_SetRowTypeAsync_puts_the_type_name_to_the_row_route()
    {
        var (client, stub) = ClientFor(HttpStatusCode.NoContent);

        Assert.True((await client.SetRowTypeAsync(3, 2, SeatType.VIP)).Success);

        Assert.Equal(HttpMethod.Put, stub.Method);
        Assert.Equal("/api/auditoriums/3/rows/2/type", stub.Path);
        using var body = JsonDocument.Parse(stub.Body!);
        Assert.Equal("VIP", body.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task US128_Ac2_A_failed_row_change_is_reported_so_the_page_can_revert_the_row()
    {
        var (gone, _) = ClientFor(HttpStatusCode.NotFound, """{"title":"Not Found","status":404}""", "application/problem+json");
        var offline = new AuditoriumApiClient(new StubFactory(new ThrowingHandler()));

        var notFound = await gone.SetRowTypeAsync(3, 2, SeatType.Standard);
        var unreachable = await offline.SetRowTypeAsync(3, 2, SeatType.Standard);

        Assert.False(notFound.Success);
        Assert.True(notFound.NotFound);
        Assert.Equal(AuditoriumApiClient.NotFoundMessage, notFound.Problem!.General());
        Assert.False(unreachable.Success);
        Assert.Same(ApiProblem.Unreachable, unreachable.Problem);
    }

    [Fact]
    public async Task US128_Ac3_GetPricesAsync_reads_the_standard_and_vip_prices()
    {
        var (client, stub) = ClientFor(HttpStatusCode.OK, """{"standardSeat":140,"vipSeat":200}""");

        var result = await client.GetPricesAsync();

        Assert.True(result.Success);
        Assert.Equal(new PricingDto(140, 200), result.Prices);
        Assert.Equal("/api/pricing", stub.Path);
    }

    [Fact]
    public async Task US128_Ac4_Duplicate_name_and_range_errors_map_to_their_own_fields()
    {
        var (client, _) = ClientFor(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"Name":["An auditorium with this name already exists"],"rows":["Rows must be a whole number between 1 and 26"],"SeatsPerRow":["Seats per row must be a whole number between 1 and 40"]}}""",
            "application/problem+json");

        var result = await client.CreateAsync(Sal4 with { Rows = 0 });

        Assert.False(result.Success);
        Assert.Equal("An auditorium with this name already exists", result.Problem!.Field("Name"));
        Assert.Equal("Rows must be a whole number between 1 and 26", result.Problem.Field("Rows"));
        Assert.Equal("Seats per row must be a whole number between 1 and 40", result.Problem.Field("SeatsPerRow"));
        Assert.Null(result.Problem.General(Fields));
    }

    [Fact]
    public async Task US128_Ac4_A_409_layout_conflict_returns_the_API_message_as_a_general_error()
    {
        var (client, _) = ClientFor(HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"Layout cannot be changed because the auditorium has bookings."}""",
            "application/problem+json");

        var result = await client.UpdateAsync(7, Sal4);

        Assert.False(result.Success);
        Assert.False(result.NotFound);
        Assert.Equal("Layout cannot be changed because the auditorium has bookings.", result.Problem!.General(Fields));
        Assert.Null(result.Problem.Field("Rows"));
    }

    [Fact]
    public async Task US128_Ac5_A_403_shows_the_permission_message()
    {
        var (client, _) = ClientFor(HttpStatusCode.Forbidden, """{"title":"You do not have access to this resource"}""", "application/problem+json");

        var result = await client.CreateAsync(Sal4);

        Assert.False(result.Success);
        Assert.Equal(AuditoriumApiClient.ForbiddenMessage, result.Problem!.General());
    }

    [Fact]
    public async Task US128_Ac6_A_dead_connection_or_unreadable_body_is_reported_instead_of_throwing()
    {
        var offline = new AuditoriumApiClient(new StubFactory(new ThrowingHandler()));
        var (garbled, _) = ClientFor(HttpStatusCode.OK, "<html></html>", "text/html");
        var (empty, _) = ClientFor(HttpStatusCode.OK, "null");

        var unreachable = await offline.ListAsync();
        var unexpected = await garbled.ListAsync();
        var noBody = await empty.GetAsync(1);

        Assert.False(unreachable.Success);
        Assert.Same(ApiProblem.Unreachable, unreachable.Problem);
        Assert.False(unexpected.Success);
        Assert.Same(ApiProblem.Unexpected, unexpected.Problem);
        Assert.False(noBody.Success);
        Assert.Same(ApiProblem.Unexpected, noBody.Problem);
    }
}
