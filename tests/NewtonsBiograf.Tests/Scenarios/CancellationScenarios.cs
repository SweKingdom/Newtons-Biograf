using System.Net;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Tests.Infrastructure;

namespace NewtonsBiograf.Tests.Scenarios;

/// <summary>US-141 AC 3: the cancellation limit (2 hours, inclusive) and who may cancel. The clock is fake.</summary>
[Collection("Scenarios")]
public sealed class CancellationScenarios : IDisposable
{
    private readonly TestDb _db = TestDb.InMemory();
    private readonly ApiFactory _api;
    private readonly int _showing;
    private readonly int _booking;

    public CancellationScenarios()
    {
        _api = new ApiFactory(_db, new FakeClock());
        _showing = _db.AddShowing(TestDb.Start);
        _booking = _db.AddBooking(_showing, TestDb.CustomerA, _db.Standard[0], _db.Standard[1]);
    }

    public void Dispose()
    {
        _api.Dispose();
        _db.Dispose();
    }

    private Task<BookingResult> Cancel(string userId, string role = Roles.Customer, params string[] permissions) =>
        _api.With<BookingService, BookingResult>(s => s.CancelAsync(_booking, ApiFactory.Principal(userId, role, permissions)));

    private void AssertStillConfirmed()
    {
        var saved = _db.GetBooking(_booking);
        Assert.Equal(BookingStatus.Confirmed, saved.Status);
        Assert.Null(saved.CancelledAt);
        Assert.All(saved.Seats, s => Assert.True(s.IsActive));
        Assert.Empty(_api.Cancelled.Raised);
    }

    [Fact]
    public async Task Customer_cancels_three_hours_before_start_and_the_seats_can_be_booked_again()
    {
        _api.Clock.UtcNow = TestDb.Start.AddHours(-3);

        Assert.IsType<BookingCancelledOk>(await Cancel(TestDb.CustomerA));

        var saved = _db.GetBooking(_booking);
        Assert.Equal(BookingStatus.Cancelled, saved.Status);
        Assert.Equal(_api.Clock.UtcNow, saved.CancelledAt);
        Assert.All(saved.Seats, s => Assert.False(s.IsActive));
        var raised = Assert.Single(_api.Cancelled.Raised);
        Assert.Equal((_booking, _showing, TestDb.CustomerA), (raised.BookingId, raised.ShowingId, raised.UserId));
        var rebook = await _api.With<BookingService, BookingResult>(s => s.CreateAsync(TestDb.CustomerB,
            new CreateBookingRequest(_showing, [_db.Standard[0], _db.Standard[1]])));
        Assert.IsType<BookingOk>(rebook);
    }

    [Fact]
    public async Task Customer_cannot_cancel_one_hour_before_start()
    {
        _api.Clock.UtcNow = TestDb.Start.AddHours(-1);

        var result = await Cancel(TestDb.CustomerA);

        Assert.Equal(2, Assert.IsType<BookingTooLate>(result).LimitHours);
        AssertStillConfirmed();
    }

    [Fact]
    public async Task Customer_cancelling_one_hour_before_start_gets_409_through_the_API()
    {
        _api.Clock.UtcNow = TestDb.Start.AddHours(-1);

        var response = await _api.Client(TestDb.CustomerA).PostAsync($"/api/bookings/{_booking}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        AssertStillConfirmed();
    }

    [Fact]
    public async Task Customer_can_cancel_exactly_two_hours_before_start()
    {
        _api.Clock.UtcNow = TestDb.Start.AddHours(-2);

        Assert.IsType<BookingCancelledOk>(await Cancel(TestDb.CustomerA));
        Assert.Equal(BookingStatus.Cancelled, _db.GetBooking(_booking).Status);
    }

    [Fact]
    public async Task Customer_cannot_cancel_one_minute_inside_the_two_hour_limit()
    {
        _api.Clock.UtcNow = TestDb.Start.AddHours(-2).AddMinutes(1);

        Assert.IsType<BookingTooLate>(await Cancel(TestDb.CustomerA));
        AssertStillConfirmed();
    }

    [Theory]
    [InlineData(Roles.Staff, new[] { Permissions.ManageBookings })]
    [InlineData(Roles.Admin, new string[0])]
    public async Task Staff_with_ManageBookings_and_Admin_cancel_one_hour_before_start(string role, string[] permissions)
    {
        _api.Clock.UtcNow = TestDb.Start.AddHours(-1);

        Assert.IsType<BookingCancelledOk>(await Cancel("staff-1", role, permissions));

        Assert.Equal(BookingStatus.Cancelled, _db.GetBooking(_booking).Status);
        Assert.Single(_api.Cancelled.Raised);
    }

    [Fact]
    public async Task Staff_without_ManageBookings_get_403()
    {
        _api.Clock.UtcNow = TestDb.Start.AddHours(-1);

        var response = await _api.Client("staff-1", Roles.Staff, Permissions.ManageMovies)
            .PostAsync($"/api/bookings/{_booking}/cancel", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertStillConfirmed();
    }
}
