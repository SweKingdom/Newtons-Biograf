using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Events;
using NewtonsBiograf.Api.Pricing;
using NewtonsBiograf.Shared;
using System.Security.Cryptography;

namespace NewtonsBiograf.Api.Services;

/// <summary>Raised once a booking has been cancelled and the change is committed.</summary>
public sealed record BookingCancelled(int BookingId, string Reference, int ShowingId, string UserId);

/// <summary>Receives booking events after the database transaction has committed.</summary>
public interface IBookingEvents
{
    Task BookingCancelledAsync(BookingCancelled e, CancellationToken ct = default);
}

/// <summary>Default subscriber: writes the event to the log (customer notification is out of scope).</summary>
public sealed class LoggingBookingEvents(ILogger<LoggingBookingEvents> logger) : IBookingEvents
{
    public Task BookingCancelledAsync(BookingCancelled e, CancellationToken ct = default)
    {
        logger.LogInformation("Booking {Reference} (id {BookingId}) cancelled for showing {ShowingId}",
            e.Reference, e.BookingId, e.ShowingId);
        return Task.CompletedTask;
    }
}

public abstract record BookingResult;

/// <summary>The booking was saved (201).</summary>
public sealed record BookingOk(BookingDto Booking) : BookingResult;

/// <summary>The seat selection is invalid (400); nothing was saved.</summary>
public sealed record BookingInvalid(string Message) : BookingResult;

/// <summary>The showing does not exist (404).</summary>
public sealed record BookingNotFound : BookingResult;

/// <summary>The showing is cancelled or has started (409); nothing was saved.</summary>
public sealed record BookingShowingClosed : BookingResult;

/// <summary>A seat is already taken (409); carries the current seat map. Nothing was saved.</summary>
public sealed record BookingConflict(SeatMapDto SeatMap) : BookingResult;

public class BookingService(AppDbContext db, IPriceCalculator prices, TimeProvider clock, IBookingEventPublisher publisher)
{
    public const int MaxSeats = 8;
    public const int ReferenceLength = 8;
    public const int MaxReferenceRetries = 5;

    /// <summary>Uppercase letters and digits without the ambiguous 0, O, 1 and I.</summary>
    public const string ReferenceAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// Books the seats in one save: the booking and its seats are written together or not at all. The unique index on
    /// active (ShowingId, SeatId) decides a race, so a violation rolls everything back and returns a conflict.
    /// A duplicate reference is regenerated and the save retried up to <see cref="MaxReferenceRetries"/> times.
    /// Raises BookingCreated after the commit.
    /// </summary>
    public virtual async Task<BookingResult> CreateAsync(string userId, CreateBookingRequest request, CancellationToken ct = default)
    {
        var ids = request.SeatIds;
        if (ids.Count is < 1 or > MaxSeats)
        {
            return new BookingInvalid($"Choose between 1 and {MaxSeats} seats.");
        }
        if (ids.Distinct().Count() != ids.Count)
        {
            return new BookingInvalid("Each seat can only be chosen once.");
        }

        var showing = await db.Showings.AsNoTracking().Include(s => s.Movie).Include(s => s.Auditorium)
            .SingleOrDefaultAsync(s => s.Id == request.ShowingId, ct);
        if (showing is null)
        {
            return new BookingNotFound();
        }
        var now = clock.GetUtcNow().UtcDateTime;
        if (showing.Status != ShowingStatus.Scheduled || showing.StartTime <= now)
        {
            return new BookingShowingClosed();
        }

        var seats = await db.Seats.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && s.AuditoriumId == showing.AuditoriumId)
            .OrderBy(s => s.RowNumber).ThenBy(s => s.SeatNumber)
            .ToListAsync(ct);
        if (seats.Count != ids.Count)
        {
            return new BookingInvalid("One or more seats do not exist in this showing's auditorium.");
        }

        // Fast path; the unique index below is what guarantees it when two requests race.
        if (await db.BookingSeats.AnyAsync(bs => bs.ShowingId == showing.Id && bs.IsActive && ids.Contains(bs.SeatId), ct))
        {
            return await ConflictAsync(showing.Id, ct);
        }

        var lines = seats.Select(s => (Seat: s, Price: prices.GetPrice(s.Type))).ToList();
        Booking booking;
        for (var retry = 0; ; retry++)
        {
            booking = new Booking
            {
                UserId = userId, ShowingId = showing.Id, Reference = NewReference(), Status = BookingStatus.Confirmed,
                TotalPrice = lines.Sum(l => l.Price), CreatedAt = now,
                Seats = lines.Select(l => new BookingSeat { SeatId = l.Seat.Id, ShowingId = showing.Id, Price = l.Price }).ToList()
            };
            db.Bookings.Add(booking);
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 } sqlite
                && sqlite.Message.Contains("UNIQUE constraint failed"))
            {
                db.ChangeTracker.Clear();
                if (sqlite.Message.Contains("BookingSeat."))
                {
                    return await ConflictAsync(showing.Id, ct);
                }
                if (!sqlite.Message.Contains("Booking.Reference") || retry == MaxReferenceRetries)
                {
                    throw;
                }
            }
        }

        publisher.RaiseCreated(booking.Id, userId, showing.Id);
        var start = DateTime.SpecifyKind(showing.StartTime, DateTimeKind.Utc);
        return new BookingOk(new BookingDto(booking.Id, booking.Reference, booking.TotalPrice, booking.Status,
            lines.Select(l => new BookingSeatDto(l.Seat.RowNumber, l.Seat.SeatNumber, l.Seat.Type, l.Price)).ToList(),
            showing.Movie!.Title, showing.Auditorium!.Name, start));
    }

    /// <summary>A random reference of <see cref="ReferenceLength"/> characters from <see cref="ReferenceAlphabet"/>.</summary>
    public virtual string NewReference() =>
        new(Enumerable.Range(0, ReferenceLength)
            .Select(_ => ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)]).ToArray());

    /// <summary>
    /// Cancels a booking and frees its seats. Does not save, open a transaction or raise events: the caller owns
    /// the transaction and raises <see cref="BookingCancelled"/> after commit. <paramref name="booking"/> must have its Seats loaded.
    /// </summary>
    public virtual Task<Booking> CancelInternalAsync(Booking booking, CancellationToken ct = default)
    {
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;
        foreach (var seat in booking.Seats)
        {
            seat.IsActive = false;
        }
        return Task.FromResult(booking);
    }

    private async Task<BookingResult> ConflictAsync(int showingId, CancellationToken ct)
    {
        var map = await SeatMapBuilder.BuildAsync(db, prices, showingId, clock, ct);
        return map is null ? new BookingShowingClosed() : new BookingConflict(map);
    }
}
