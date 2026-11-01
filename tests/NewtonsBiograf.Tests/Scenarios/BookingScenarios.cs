using System.Text.RegularExpressions;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Tests.Infrastructure;

namespace NewtonsBiograf.Tests.Scenarios;

/// <summary>US-141 AC 1 and AC 2: booking seats and double booking, on a real SQLite database.</summary>
[Collection("Scenarios")]
public sealed class BookingScenarios : IDisposable
{
    private readonly TestDb _db = TestDb.InMemory();
    private readonly ApiFactory _api;

    public BookingScenarios() => _api = new ApiFactory(_db, new FakeClock());

    public void Dispose()
    {
        _api.Dispose();
        _db.Dispose();
    }

    private static Task<BookingResult> Book(ApiFactory api, string userId, int showingId, params int[] seatIds) =>
        api.With<BookingService, BookingResult>(s => s.CreateAsync(userId, new CreateBookingRequest(showingId, seatIds.ToList())));

    [Fact]
    public async Task Customer_books_three_seats_gets_confirmed_booking_with_total_and_reference()
    {
        var showing = _db.AddShowing(TestDb.Start);

        var ok = Assert.IsType<BookingOk>(await Book(_api, TestDb.CustomerA, showing, _db.Standard[0], _db.Standard[1], _db.Vip));

        var booking = ok.Booking;
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(2 * 140 + 200, booking.TotalPrice);
        Assert.Matches(new Regex($"^[{BookingService.ReferenceAlphabet}]{{8}}$"), booking.Reference);
        Assert.Equal(3, booking.Seats.Count);
        var saved = _db.GetBooking(booking.Id);
        Assert.Equal(3, saved.Seats.Count(s => s.IsActive));
        Assert.Equal(480m, saved.TotalPrice);
        Assert.Equal(saved.TotalPrice, saved.Seats.Sum(s => s.Price));
        var raised = Assert.Single(_api.Created);
        Assert.Equal((booking.Id, TestDb.CustomerA, showing), (raised.BookingId, raised.UserId, raised.ShowingId));
    }

    [Fact]
    public async Task Booking_a_taken_seat_is_rejected_and_leaves_no_partial_rows()
    {
        var showing = _db.AddShowing(TestDb.Start);
        Assert.IsType<BookingOk>(await Book(_api, TestDb.CustomerA, showing, _db.Standard[0], _db.Standard[1]));
        var raisedBefore = _api.Created.Count;

        var result = await Book(_api, TestDb.CustomerB, showing, _db.Standard[1], _db.Standard[2]);

        var conflict = Assert.IsType<BookingConflict>(result);
        var taken = conflict.SeatMap.Seats.Where(s => s.IsTaken).Select(s => s.SeatId).Order();
        Assert.Equal(new[] { _db.Standard[0], _db.Standard[1] }.Order(), taken);
        Assert.Equal((1, 2), _db.Counts()); // the free seat of the failed booking was not saved either
        Assert.Equal(raisedBefore, _api.Created.Count);
    }

    [Fact]
    public async Task Two_parallel_bookings_of_the_same_seat_give_one_success_and_one_conflict()
    {
        using var file = TestDb.File();
        using var api = new ApiFactory(file, new FakeClock());
        for (var round = 1; round <= 5; round++)
        {
            var showing = file.AddShowing(TestDb.Start.AddHours(round * 3));

            var results = await Task.WhenAll(
                Task.Run(() => Book(api, TestDb.CustomerA, showing, file.Standard[0], file.Standard[1])),
                Task.Run(() => Book(api, TestDb.CustomerB, showing, file.Standard[1], file.Standard[2])));

            Assert.Single(results, r => r is BookingOk);
            Assert.Single(results, r => r is BookingConflict);
            Assert.Equal((round, round * 2), file.Counts());
        }
    }
}
