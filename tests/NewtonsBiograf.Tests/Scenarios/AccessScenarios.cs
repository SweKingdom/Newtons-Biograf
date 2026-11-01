using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NewtonsBiograf.Tests.Infrastructure;

namespace NewtonsBiograf.Tests.Scenarios;

/// <summary>US-141 AC 5: a customer sees only their own bookings; staff with ManageBookings see all. Runs through the API.</summary>
[Collection("Scenarios")]
public sealed class AccessScenarios : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StrictEnumConverterFactory() }
    };

    private readonly TestDb _db = TestDb.InMemory();
    private readonly ApiFactory _api;
    private readonly int _showing;
    private readonly int _bookingOfA;
    private readonly int _bookingOfB;

    public AccessScenarios()
    {
        _api = new ApiFactory(_db, new FakeClock());
        _showing = _db.AddShowing(TestDb.Start);
        _bookingOfA = _db.AddBooking(_showing, TestDb.CustomerA, _db.Standard[0]);
        _bookingOfB = _db.AddBooking(_showing, TestDb.CustomerB, _db.Standard[1]);
    }

    public void Dispose()
    {
        _api.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task Customer_reading_another_customers_booking_gets_403()
    {
        var response = await _api.Client(TestDb.CustomerB).GetAsync($"/api/bookings/{_bookingOfA}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_reading_their_own_booking_gets_200()
    {
        var response = await _api.Client(TestDb.CustomerA).GetAsync($"/api/bookings/{_bookingOfA}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_bookingOfA, (await response.Content.ReadFromJsonAsync<BookingDto>(Json))!.Id);
    }

    [Fact]
    public async Task Unauthenticated_request_for_a_booking_gets_401()
    {
        var response = await _api.Client(null).GetAsync($"/api/bookings/{_bookingOfA}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customer_listing_all_bookings_gets_403()
    {
        var response = await _api.Client(TestDb.CustomerA).GetAsync($"/api/bookings?showingId={_showing}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Staff_with_ManageBookings_read_any_booking_and_list_them()
    {
        var client = _api.Client("staff-1", Roles.Staff, Permissions.ManageBookings);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/bookings/{_bookingOfA}")).StatusCode);
        var list = await client.GetFromJsonAsync<List<BookingDto>>($"/api/bookings?showingId={_showing}", Json);
        Assert.Equal(new[] { _bookingOfA, _bookingOfB }.Order(), list!.Select(b => b.Id).Order());
    }

    [Fact]
    public async Task Mine_returns_only_the_callers_own_bookings()
    {
        var mine = await _api.Client(TestDb.CustomerB).GetFromJsonAsync<List<BookingDto>>("/api/bookings/mine?scope=upcoming", Json);

        Assert.Equal(_bookingOfB, Assert.Single(mine!).Id);
    }
}
