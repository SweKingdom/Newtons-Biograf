using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Client.Components;
using NewtonsBiograf.Client.Pages;
using NewtonsBiograf.Client.Services;
using static NewtonsBiograf.Tests.MovieClientTests;

namespace NewtonsBiograf.Tests;

/// <summary>US-137: seat selection and my-bookings screens — typed client, selection rules and server-rendered page states.</summary>
public class BookingClientTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StrictEnumConverterFactory() }
    };

    private static readonly DateTime Start = new(2030, 1, 15, 17, 0, 0, DateTimeKind.Utc);

    /// <summary>Row 1: Standard seats 1–3 (ids 1–3, seat 2 taken). Row 2: VIP seats 1–3 (ids 4–6).</summary>
    private static SeatMapDto Map(bool bookable = true, params int[] takenIds) => new(7, Start, bookable,
        [
            .. Enumerable.Range(1, 3).Select(n => new SeatDto(n, 1, n, SeatType.Standard, 100, n == 2 || takenIds.Contains(n))),
            .. Enumerable.Range(1, 3).Select(n => new SeatDto(3 + n, 2, n, SeatType.VIP, 150, takenIds.Contains(3 + n)))
        ]);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static BookingDto Booking(BookingStatus status = BookingStatus.Confirmed) => new(11, "ABCD2345", 250, status,
        [new BookingSeatDto(1, 3, SeatType.Standard, 100), new BookingSeatDto(2, 1, SeatType.VIP, 150)],
        "Dune", "Salong 1", Start, Start.AddDays(-1));

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string? json = null, string contentType = "application/json") =>
        new(status) { Content = json is null ? null : new StringContent(json, Encoding.UTF8, contentType) };

    private static (BookingApiClient Client, StubHandler Stub) ClientFor(HttpStatusCode status, string? json = null, string contentType = "application/json")
    {
        var stub = new StubHandler(() => Reply(status, json, contentType));
        return (new BookingApiClient(new StubFactory(stub)), stub);
    }

    /// <summary>Renders a page or component to HTML on the server, with the API answered by <paramref name="api"/>.</summary>
    private static async Task<string> RenderAsync<T>(Func<HttpRequestMessage, HttpResponseMessage>? api, Dictionary<string, object?> parameters)
        where T : IComponent
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IHttpClientFactory>(new StubFactory(new RouteHandler(api ?? (_ => Reply(HttpStatusCode.NotFound)))));
        services.AddSingleton<BookingApiClient>();
        services.AddSingleton<ShowingApiClient>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }

    private static HttpResponseMessage Route(HttpRequestMessage request, SeatMapDto? map, BookingDto[]? mine = null)
    {
        var path = request.RequestUri!.AbsolutePath;
        return path switch
        {
            "/api/showings/settings" => Reply(HttpStatusCode.OK, Serialize(new ShowingSettingsDto(15, "Europe/Stockholm"))),
            "/api/showings/7/seats" when map is not null => Reply(HttpStatusCode.OK, Serialize(map)),
            "/api/bookings/mine" when mine is not null => Reply(HttpStatusCode.OK, Serialize(mine)),
            _ => Reply(HttpStatusCode.NotFound)
        };
    }

    private static Task<string> RenderBookAsync(Func<HttpRequestMessage, HttpResponseMessage> api) =>
        RenderAsync<Book>(api, new() { ["ShowingId"] = 7 });

    private static string ButtonTag(string html, string label)
    {
        var match = Regex.Match(html, $"<button[^>]*aria-label=\"{Regex.Escape(label)}\"[^>]*>");
        Assert.True(match.Success, $"No button labelled '{label}'");
        return match.Value;
    }

    // ---- AC 1: seat map ----

    [Fact]
    public async Task Ac1_Seat_map_has_a_labelled_button_per_seat_with_a_cue_for_each_state_and_a_four_state_legend()
    {
        var map = Map();
        var html = await RenderAsync<SeatGrid>(null, new()
        {
            ["Rows"] = 2, ["SeatsPerRow"] = 3, ["VipRows"] = new HashSet<int> { 2 }, ["Seats"] = map.Seats,
            ["Selectable"] = true, ["SelectedIds"] = new HashSet<int> { 1 },
            ["OnSeatClick"] = EventCallback.Factory.Create<(int, int)>(this, _ => { })
        });

        var selected = ButtonTag(html, "Row 1, seat 1, Standard, selected");
        Assert.Contains("seat-selected", selected);
        Assert.Contains("aria-pressed=\"true\"", selected);
        var taken = ButtonTag(html, "Row 1, seat 2, Standard, taken");
        Assert.Contains("seat-taken", taken);
        Assert.Contains("disabled", taken);
        var vip = ButtonTag(html, "Row 2, seat 1, VIP, free");
        Assert.Contains("seat-vip", vip);
        Assert.Contains("aria-pressed=\"false\"", vip);
        Assert.Matches("<button[^>]*Row 1, seat 1, Standard, selected[^>]*><span aria-hidden=\"true\">1✓</span>", html);
        Assert.Contains(">2✕<", html);
        Assert.Contains(">1★<", html);
        foreach (var legend in new[] { "Free Standard", "Free VIP", "Taken", "Selected" })
        {
            Assert.Contains(legend, html);
        }
    }

    [Fact]
    public async Task Ac1_Seat_grid_that_is_not_selectable_draws_no_seat_buttons()
    {
        var html = await RenderAsync<SeatGrid>(null, new()
        {
            ["Rows"] = 2, ["SeatsPerRow"] = 3, ["VipRows"] = new HashSet<int> { 2 }, ["Seats"] = Map().Seats
        });

        Assert.DoesNotContain("<button", html);
        Assert.Contains("aria-label=\"Row 2, seat 3, VIP, free\"", html);
    }

    [Fact]
    public void Ac1_Admin_seat_grid_without_seats_keeps_its_two_item_legend()
    {
        var source = File.ReadAllText(Path.Combine(SolutionRoot(), "src", "NewtonsBiograf.Client", "Components", "SeatGrid.razor"));

        Assert.Contains("<li><span class=\"seat seat-standard\" aria-hidden=\"true\"></span> Standard</li>", source);
        Assert.Contains("OnRowClick", source);
    }

    [Fact]
    public void Ac1_The_ninth_seat_is_refused_with_a_message_and_a_selected_seat_can_be_toggled_off()
    {
        var seats = Enumerable.Range(1, 9).Select(n => new SeatDto(n, 1, n, SeatType.Standard, 100, false)).ToList();
        var selected = new HashSet<int>();

        foreach (var seat in seats.Take(8))
        {
            Assert.Null(SeatSelection.Toggle(selected, seat));
        }
        var message = SeatSelection.Toggle(selected, seats[8]);

        Assert.Equal("You can select at most 8 seats.", message);
        Assert.Equal(8, selected.Count);
        Assert.DoesNotContain(9, selected);
        Assert.Null(SeatSelection.Toggle(selected, seats[0]));
        Assert.Equal(7, selected.Count);
        Assert.Null(SeatSelection.Toggle(selected, seats[8]));
        Assert.Contains(9, selected);
    }

    [Fact]
    public void Ac1_A_taken_seat_cannot_be_selected()
    {
        var selected = new HashSet<int>();

        SeatSelection.Toggle(selected, Map().Seats[1]);

        Assert.Empty(selected);
        Assert.Equal(BookingLimits.MaxSeats, BookingService.MaxSeats);
        Assert.Equal(8, BookingLimits.MaxSeats);
    }

    // ---- AC 2: running total ----

    [Fact]
    public async Task Ac2_The_total_is_the_sum_of_the_selected_seat_prices_and_Book_is_disabled_with_no_seats()
    {
        var map = Map();
        Assert.Equal(0, SeatSelection.Total(map, new HashSet<int>()));
        Assert.Equal(100 + 150 + 150, SeatSelection.Total(map, new HashSet<int> { 1, 4, 6 }));

        var html = await RenderBookAsync(r => Route(r, map));

        Assert.Contains("Selected seats: <strong>0</strong>", html);
        Assert.Contains("Total: <strong>0 SEK</strong>", html);
        Assert.Matches("<button[^>]*disabled[^>]*>Book</button>", html);
        Assert.Contains("role=\"status\"", html);
    }

    // ---- AC 3: confirmation ----

    [Fact]
    public async Task Ac3_CreateAsync_posts_the_seats_and_returns_the_booking()
    {
        var (client, stub) = ClientFor(HttpStatusCode.Created, Serialize(Booking()));

        var result = await client.CreateAsync(new CreateBookingRequest(7, [1, 4]));

        Assert.Equal(HttpMethod.Post, stub.Method);
        Assert.Equal("/api/bookings", stub.Path);
        Assert.Contains("\"showingId\":7", stub.Body);
        Assert.Contains("\"seatIds\":[1,4]", stub.Body);
        Assert.Equal("ABCD2345", result.Booking!.Reference);
        Assert.Null(result.Conflict);
        Assert.Null(result.Error);
        Assert.Equal("Row 1 seat 3 (Standard), Row 2 seat 1 (VIP)", BookingText.Seats(result.Booking));
        Assert.Equal("250 SEK", BookingText.Sek(result.Booking.TotalPrice));
    }

    [Fact]
    public void Ac3_The_confirmation_shows_reference_movie_time_seats_total_and_a_link_to_my_bookings()
    {
        var page = File.ReadAllText(Path.Combine(SolutionRoot(), "src", "NewtonsBiograf.Client", "Pages", "Book.razor"));

        foreach (var fragment in new[] { "confirmation.Reference", "confirmation.MovieTitle", "Time(confirmation.Start)",
                     "BookingText.Seats(confirmation)", "BookingText.Sek(confirmation.TotalPrice)", "<a href=\"my-bookings\">" })
        {
            Assert.Contains(fragment, page);
        }
    }

    // ---- AC 4: conflict ----

    [Fact]
    public async Task Ac4_A_seat_conflict_returns_the_message_and_the_fresh_map_and_selected_taken_seats_are_dropped()
    {
        var fresh = Map(true, 1);
        var (client, _) = ClientFor(HttpStatusCode.Conflict,
            Serialize(new SeatConflictDto("One or more seats are no longer available", fresh)));

        var result = await client.CreateAsync(new CreateBookingRequest(7, [1, 3]));

        Assert.Null(result.Booking);
        Assert.Null(result.Error);
        Assert.Equal("One or more seats are no longer available", result.Conflict!.Message);
        var selected = new HashSet<int> { 1, 3 };
        SeatSelection.KeepFree(selected, result.Conflict.SeatMap);
        Assert.Equal([3], selected);
        Assert.True(result.Conflict.SeatMap.Seats.Single(s => s.SeatId == 1).IsTaken);
    }

    [Fact]
    public async Task Ac4_A_409_for_a_closed_showing_is_an_error_message_not_a_conflict()
    {
        var (client, _) = ClientFor(HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"This showing can no longer be booked."}""", "application/problem+json");

        var result = await client.CreateAsync(new CreateBookingRequest(7, [1]));

        Assert.Null(result.Conflict);
        Assert.Equal("This showing can no longer be booked.", result.Error);
    }

    [Fact]
    public async Task Ac4_A_rejected_dead_or_garbled_create_gives_a_plain_error_instead_of_throwing()
    {
        var (invalid, _) = ClientFor(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"SeatIds":["Choose between 1 and 8 seats."]}}""",
            "application/problem+json");
        var (garbled, _) = ClientFor(HttpStatusCode.Created, "<html></html>", "text/html");
        var offline = new BookingApiClient(new StubFactory(new ThrowingHandler()));

        Assert.Equal("Choose between 1 and 8 seats.", (await invalid.CreateAsync(new CreateBookingRequest(7, []))).Error);
        Assert.Equal("Something went wrong. Please try again.", (await garbled.CreateAsync(new CreateBookingRequest(7, [1]))).Error);
        Assert.Equal(ApiProblem.Unreachable.General(), (await offline.CreateAsync(new CreateBookingRequest(7, [1]))).Error);
    }

    // ---- AC 5: auth and closed showing ----

    [Fact]
    public async Task Ac5_A_map_that_is_not_bookable_shows_the_closed_message_and_no_seat_buttons_or_Book()
    {
        var html = await RenderBookAsync(r => Route(r, Map(bookable: false)));

        Assert.Contains("This showing can no longer be booked", html);
        Assert.DoesNotContain("<button", html);
        Assert.DoesNotContain("Selected seats", html);
    }

    [Fact]
    public async Task Ac5_An_unknown_showing_shows_a_not_found_message()
    {
        var html = await RenderBookAsync(r => Route(r, null));

        Assert.Contains("This showing does not exist", html);
        Assert.DoesNotContain("seat-grid", html);
    }

    [Fact]
    public async Task Ac5_GetSeatMapAsync_reads_the_map_and_flags_a_404()
    {
        var (client, stub) = ClientFor(HttpStatusCode.OK, Serialize(Map()));
        var (missing, _) = ClientFor(HttpStatusCode.NotFound, """{"title":"Not Found","status":404}""", "application/problem+json");

        var found = await client.GetSeatMapAsync(7);
        var gone = await missing.GetSeatMapAsync(9999);

        Assert.Equal("/api/showings/7/seats", stub.Path);
        Assert.Equal(6, found.Value!.Seats.Count);
        Assert.Equal(SeatType.VIP, found.Value.Seats[5].Type);
        Assert.True(gone.NotFound);
    }

    [Fact]
    public void Ac5_The_pages_are_customer_only_and_the_client_registers_the_same_policy_as_the_API()
    {
        Assert.Equal("/showings/{ShowingId:int}/book", typeof(Book).GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal("/my-bookings", typeof(MyBookings).GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal("CustomerOnly", typeof(Book).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal("CustomerOnly", typeof(MyBookings).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal("CustomerOnly", NewtonsBiograf.Api.Auth.Policies.CustomerOnly);

        var program = File.ReadAllText(Path.Combine(SolutionRoot(), "src", "NewtonsBiograf.Client", "Program.cs"));
        Assert.Contains("AddPolicy(PolicyNames.CustomerOnly, policy => policy.RequireRole(Roles.Customer))", program);
        Assert.Contains("AddScoped<BookingApiClient>", program);
        var layout = File.ReadAllText(Path.Combine(SolutionRoot(), "src", "NewtonsBiograf.Client", "Layout", "MainLayout.razor"));
        var entry = layout.IndexOf("href=\"my-bookings\"", StringComparison.Ordinal);
        var block = layout.LastIndexOf("<AuthorizeView Policy=\"@PolicyNames.CustomerOnly\">", entry, StringComparison.Ordinal);
        Assert.True(entry > block && block >= 0);
    }

    // ---- AC 6: my bookings ----

    [Fact]
    public async Task Ac6_My_bookings_has_tabs_and_lists_reference_movie_time_seats_total_and_status()
    {
        var html = await RenderAsync<MyBookings>(r => Route(r, null, [Booking()]), []);

        Assert.Contains("role=\"tablist\"", html);
        Assert.Matches("<button[^>]*id=\"tab-upcoming\"[^>]*aria-selected=\"true\"", html);
        Assert.Matches("<button[^>]*id=\"tab-history\"[^>]*aria-selected=\"false\"", html);
        Assert.Contains(">ABCD2345<", html);
        Assert.Contains(">Dune<", html);
        Assert.Contains(">Tue 15 Jan 2030 18:00<", html);
        Assert.Contains(">Row 1 seat 3 (Standard), Row 2 seat 1 (VIP)<", html);
        Assert.Contains(">250 SEK<", html);
        Assert.Contains(">Confirmed<", html);
    }

    [Fact]
    public async Task Ac6_My_bookings_shows_an_empty_state_and_an_error_state()
    {
        var empty = await RenderAsync<MyBookings>(r => Route(r, null, []), []);
        var failed = await RenderAsync<MyBookings>(r => r.RequestUri!.AbsolutePath == "/api/bookings/mine"
            ? Reply(HttpStatusCode.InternalServerError, """{"title":"Server error","status":500}""", "application/problem+json")
            : Route(r, null), []);

        Assert.Contains("You have no upcoming bookings.", empty);
        Assert.DoesNotContain("<table", empty);
        Assert.Contains("Server error", failed);
        Assert.DoesNotContain("<table", failed);
    }

    [Theory]
    [InlineData(BookingScope.Upcoming, "upcoming")]
    [InlineData(BookingScope.History, "history")]
    public async Task Ac6_GetMineAsync_asks_for_the_tab_scope(BookingScope scope, string expected)
    {
        Uri? uri = null;
        var handler = new RouteHandler(request =>
        {
            uri = request.RequestUri;
            return Reply(HttpStatusCode.OK, Serialize(new[] { Booking(BookingStatus.Cancelled) }));
        });
        var client = new BookingApiClient(new StubFactory(handler));

        var result = await client.GetMineAsync(scope);

        Assert.Equal($"?scope={expected}", uri!.Query);
        Assert.Equal(BookingStatus.Cancelled, Assert.Single(result.Value!).Status);
    }

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewtonsBiograf.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Solution root not found.");
    }
}
