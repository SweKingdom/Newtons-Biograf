using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Client.Pages;
using NewtonsBiograf.Client.Services;
using static NewtonsBiograf.Tests.MovieClientTests;
using StaffBookings = NewtonsBiograf.Client.Pages.Staff.Bookings;

namespace NewtonsBiograf.Tests;

/// <summary>US-139: cancel buttons on My bookings, the staff booking screen, the typed client calls and the client-side cancel rules.</summary>
public class CancellationClientTests
{
    private const string TooLate = "Bookings can only be cancelled up to 2 hours before the showing starts.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StrictEnumConverterFactory() }
    };

    private static BookingDto Booking(DateTime start, BookingStatus status = BookingStatus.Confirmed) => new(11, "ABCD2345", 250, status,
        [new BookingSeatDto(1, 3, SeatType.Standard, 100), new BookingSeatDto(2, 1, SeatType.VIP, 150)],
        "Dune", "Salong 1", start, start.AddDays(-1));

    private static HttpResponseMessage Reply(HttpStatusCode status, string? json = null, string contentType = "application/json") =>
        new(status) { Content = json is null ? null : new StringContent(json, Encoding.UTF8, contentType) };

    private static (BookingApiClient Client, StubHandler Stub) ClientFor(HttpStatusCode status, string? json = null, string contentType = "application/json")
    {
        var stub = new StubHandler(() => Reply(status, json, contentType));
        return (new BookingApiClient(new StubFactory(stub)), stub);
    }

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static async Task<string> RenderAsync<T>(Func<HttpRequestMessage, HttpResponseMessage> api) where T : IComponent
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IHttpClientFactory>(new StubFactory(new RouteHandler(api)));
        services.AddSingleton<BookingApiClient>();
        services.AddSingleton<ShowingApiClient>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<T>(ParameterView.Empty)).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }

    private static Task<string> RenderMineAsync(params BookingDto[] mine) => RenderAsync<MyBookings>(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        return path == "/api/bookings/mine" ? Reply(HttpStatusCode.OK, JsonSerializer.Serialize(mine, Json))
            : path == "/api/showings/settings" ? Reply(HttpStatusCode.OK, """{"cleaningMinutes":15,"displayTimeZone":"Europe/Stockholm"}""")
            : Reply(HttpStatusCode.NotFound);
    });

    private static string Source(params string[] path)
    {
        var parts = new[] { SolutionRoot(), "src", "NewtonsBiograf.Client" }.Concat(path).ToArray();
        return File.ReadAllText(Path.Combine(parts));
    }

    // ---- AC 1: customer cancel ----

    [Fact]
    public async Task Ac1_A_confirmed_upcoming_booking_shows_an_enabled_Cancel_button_and_no_late_message()
    {
        var html = await RenderMineAsync(Booking(DateTime.UtcNow.AddDays(3)));

        Assert.Matches("<button(?![^>]*disabled)[^>]*>Cancel</button>", html);
        Assert.DoesNotContain(TooLate, html);
    }

    [Fact]
    public async Task Ac1_CancelAsync_posts_to_the_cancel_endpoint_and_reports_success_on_204()
    {
        var (client, stub) = ClientFor(HttpStatusCode.NoContent);

        var result = await client.CancelAsync(11);

        Assert.Equal(HttpMethod.Post, stub.Method);
        Assert.Equal("/api/bookings/11/cancel", stub.Path);
        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Ac1_Cancel_asks_for_confirmation_before_calling_the_API_then_reloads_the_list()
    {
        var page = Source("Pages", "MyBookings.razor");

        Assert.Contains("Ask(booking)", page);
        Assert.Contains("role=\"alertdialog\"", page);
        Assert.Contains("Yes, cancel booking", page);
        Assert.Contains("Keep booking", page);
        Assert.Matches(@"Api\.CancelAsync\(booking\.Id\)[\s\S]*await LoadAsync\(\)", page);
    }

    // ---- AC 2: late cancel ----

    [Fact]
    public async Task Ac2_Inside_two_hours_the_Cancel_button_is_disabled_with_the_explanation_as_visible_text()
    {
        var html = await RenderMineAsync(Booking(DateTime.UtcNow.AddHours(1)));

        Assert.Matches("<button[^>]*disabled[^>]*>Cancel</button>", html);
        Assert.Contains($">{TooLate}<", html);
        Assert.Equal(TooLate, BookingText.LateCancelMessage);
    }

    [Fact]
    public void Ac2_The_limit_is_inclusive_exactly_two_hours_before_is_still_allowed()
    {
        var start = new DateTime(2030, 1, 15, 17, 0, 0, DateTimeKind.Utc);
        var booking = Booking(start);

        Assert.False(BookingText.IsTooLateForCustomer(booking, start.AddHours(-3)));
        Assert.False(BookingText.IsTooLateForCustomer(booking, start.AddHours(-2)));
        Assert.True(BookingText.IsTooLateForCustomer(booking, start.AddHours(-2).AddSeconds(1)));
        Assert.Equal(new BookingOptions().CancellationLimitHours, BookingLimits.CancellationLimitHours);
    }

    [Fact]
    public async Task Ac2_A_409_from_the_API_gives_its_own_message_and_other_refusals_get_plain_messages()
    {
        var (late, _) = ClientFor(HttpStatusCode.Conflict,
            $$"""{"title":"Conflict","status":409,"detail":"{{TooLate}}"}""", "application/problem+json");
        var (forbidden, _) = ClientFor(HttpStatusCode.Forbidden);
        var (missing, _) = ClientFor(HttpStatusCode.NotFound);
        var offline = new BookingApiClient(new StubFactory(new ThrowingHandler()));

        var lateResult = await late.CancelAsync(11);

        Assert.False(lateResult.Success);
        Assert.Equal(TooLate, lateResult.Error);
        Assert.Equal(BookingApiClient.CancelForbiddenMessage, (await forbidden.CancelAsync(11)).Error);
        Assert.Equal(BookingApiClient.CancelNotFoundMessage, (await missing.CancelAsync(11)).Error);
        Assert.Equal("Could not reach the server. Check your connection and try again.", (await offline.CancelAsync(11)).Error);
    }

    [Fact]
    public void Ac2_A_refused_cancel_shows_the_API_message_and_reloads_the_list()
    {
        var page = Source("Pages", "MyBookings.razor");

        Assert.Matches(@"actionError = result\.Error;[\s\S]*await LoadAsync\(\)", page);
        Assert.Contains("<AlertBox Kind=\"AlertBox.AlertKind.Error\">@actionError</AlertBox>", page);
    }

    // ---- AC 3: no button when not applicable ----

    [Fact]
    public async Task Ac3_Cancelled_and_past_bookings_show_no_Cancel_button()
    {
        var cancelled = await RenderMineAsync(Booking(DateTime.UtcNow.AddDays(3), BookingStatus.Cancelled));
        var past = await RenderMineAsync(Booking(DateTime.UtcNow.AddDays(-3)));

        Assert.Contains(">Cancelled<", cancelled);
        Assert.DoesNotContain(">Cancel</button>", cancelled);
        Assert.DoesNotContain(TooLate, cancelled);
        Assert.Contains(">Confirmed<", past);
        Assert.DoesNotContain(">Cancel</button>", past);
        Assert.DoesNotContain(TooLate, past);
    }

    [Fact]
    public void Ac3_CanCancel_needs_a_Confirmed_booking_whose_showing_has_not_started()
    {
        var now = new DateTime(2030, 1, 15, 17, 0, 0, DateTimeKind.Utc);

        Assert.True(BookingText.CanCancel(Booking(now.AddMinutes(1)), now));
        Assert.False(BookingText.CanCancel(Booking(now), now));
        Assert.False(BookingText.CanCancel(Booking(now.AddDays(1), BookingStatus.Cancelled), now));
    }

    // ---- AC 4: staff booking list ----

    [Fact]
    public async Task Ac4_SearchAsync_sends_the_showing_and_the_escaped_reference_and_reads_the_bookings()
    {
        var start = new DateTime(2030, 1, 15, 17, 0, 0, DateTimeKind.Utc);
        Uri? uri = null;
        var client = new BookingApiClient(new StubFactory(new RouteHandler(request =>
        {
            uri = request.RequestUri;
            return Reply(HttpStatusCode.OK, JsonSerializer.Serialize(new[] { Booking(start) }, Json));
        })));

        var both = await client.SearchAsync(7, " abcd 2345 ");
        Assert.Equal("/api/bookings", uri!.AbsolutePath);
        Assert.Equal("?showingId=7&reference=abcd%202345", uri.Query);
        Assert.Equal("ABCD2345", Assert.Single(both.Value!).Reference);

        await client.SearchAsync(null, "ABCD2345");
        Assert.Equal("?reference=ABCD2345", uri.Query);
        await client.SearchAsync(9, null);
        Assert.Equal("?showingId=9", uri.Query);
    }

    [Fact]
    public void Ac4_The_staff_page_is_on_staff_bookings_for_the_ManageBookings_policy_and_linked_from_the_menu()
    {
        Assert.Equal("/staff/bookings", typeof(StaffBookings).GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal("ManageBookings", typeof(StaffBookings).GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal("ManageBookings", NewtonsBiograf.Api.Auth.Policies.ManageBookings);

        var layout = Source("Layout", "MainLayout.razor");
        var entry = layout.IndexOf("href=\"staff/bookings\"", StringComparison.Ordinal);
        var block = layout.LastIndexOf("<AuthorizeView Policy=\"@Permissions.ManageBookings\">", entry, StringComparison.Ordinal);
        Assert.True(entry > block && block >= 0);
    }

    [Fact]
    public void Ac4_Staff_can_cancel_any_Confirmed_booking_at_any_time_after_a_confirm_panel_and_the_list_is_refreshed()
    {
        var page = Source("Pages", "Staff", "Bookings.razor");

        Assert.Contains("else if (booking.Status == BookingStatus.Confirmed)", page);
        Assert.DoesNotContain("IsTooLateForCustomer", page);
        Assert.Contains("Yes, cancel booking", page);
        Assert.Matches(@"Api\.CancelAsync\(booking\.Id\)[\s\S]*await LoadAsync\(\)", page);
        Assert.Contains("Api.SearchAsync(searchedShowing, searchedReference)", page);
    }

    [Fact]
    public async Task Ac4_The_staff_page_starts_with_the_filters_and_no_results_until_a_search_is_made()
    {
        var html = await RenderAsync<StaffBookings>(request =>
            request.RequestUri!.AbsolutePath == "/api/showings/settings"
                ? Reply(HttpStatusCode.OK, """{"cleaningMinutes":15}""")
                : Reply(HttpStatusCode.InternalServerError));

        Assert.Contains("id=\"filter-showing\"", html);
        Assert.Contains("id=\"filter-reference\"", html);
        Assert.Contains(">Search</button>", html);
        Assert.DoesNotContain("<table", html);
        Assert.DoesNotContain("No bookings found", html);
    }

    // ---- AC 5: error and empty states ----

    [Fact]
    public async Task Ac5_The_staff_search_reports_a_server_error_as_a_plain_message_and_an_empty_result_as_an_empty_list()
    {
        var (failing, _) = ClientFor(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"filter":["Give a showingId, a reference or both."]}}""",
            "application/problem+json");
        var (empty, _) = ClientFor(HttpStatusCode.OK, "[]");
        var offline = new BookingApiClient(new StubFactory(new ThrowingHandler()));

        Assert.Equal("Give a showingId, a reference or both.", (await failing.SearchAsync(null, " ")).Error);
        var none = await empty.SearchAsync(7, null);
        Assert.True(none.Success);
        Assert.Empty(none.Value!);
        Assert.Equal("Could not reach the server. Check your connection and try again.", (await offline.SearchAsync(7, null)).Error);
    }

    [Fact]
    public void Ac5_Both_pages_show_a_spinner_while_loading_and_the_staff_page_says_No_bookings_found_when_empty()
    {
        var staff = Source("Pages", "Staff", "Bookings.razor");

        Assert.Contains("<Spinner />", staff);
        Assert.Contains("No bookings found", staff);
        Assert.Contains("<AlertBox Kind=\"AlertBox.AlertKind.Error\">@error</AlertBox>", staff);
        Assert.Contains("Enter a showing id, a booking reference or both.", staff);
        Assert.Contains("<Spinner />", Source("Pages", "MyBookings.razor"));
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
