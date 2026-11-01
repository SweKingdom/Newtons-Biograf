using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

/// <summary>Booking rules bound from the "Booking" configuration section (validated on start).</summary>
public class BookingOptions
{
    public const string Section = "Booking";

    /// <summary>A customer may cancel until this many hours before the showing starts (inclusive).</summary>
    public int CancellationLimitHours { get; set; } = 2;
}

public abstract record BookingResult;

/// <summary>The booking was saved (201) or found.</summary>
public sealed record BookingOk(BookingDto Booking) : BookingResult;

/// <summary>The seat selection is invalid (400); nothing was saved.</summary>
public sealed record BookingInvalid(string Message) : BookingResult;

/// <summary>The showing or booking does not exist (404).</summary>
public sealed record BookingNotFound : BookingResult;

/// <summary>The booking belongs to someone else and the caller may not manage bookings (403).</summary>
public sealed record BookingForbidden : BookingResult;

/// <summary>The showing is cancelled or has started (409); nothing was saved.</summary>
public sealed record BookingShowingClosed : BookingResult;

/// <summary>A seat is already taken (409); carries the current seat map. Nothing was saved.</summary>
public sealed record BookingConflict(SeatMapDto SeatMap) : BookingResult;

/// <summary>The booking was cancelled and its seats freed (204).</summary>
public sealed record BookingCancelledOk : BookingResult;

/// <summary>The booking is already cancelled (409); nothing was changed.</summary>
public sealed record BookingAlreadyCancelled : BookingResult;

/// <summary>A customer asked to cancel inside the cancellation limit (409); nothing was changed.</summary>
public sealed record BookingTooLate(int LimitHours) : BookingResult;

public class BookingService(AppDbContext db, IPriceCalculator prices, TimeProvider clock, IBookingEventPublisher publisher,
    IBookingEvents events, IOptions<BookingOptions> options, ILogger<BookingService> logger)
{
    public const int MaxSeats = BookingLimits.MaxSeats;
    public const int ReferenceLength = 8;
    public const int MaxReferenceRetries = 5;
    public const int MaxSearchResults = 2 * 100;

    /// <summary>Uppercase letters and digits without the ambiguous 0, O, 1 and I.</summary>
    public const string ReferenceAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// The one projection every read query uses. Dates come back with an unspecified kind from the database, so the
    /// results go through <see cref="AsUtc"/>. Seats are the booking's rows whether or not they are still active.
    /// </summary>
    public static readonly Expression<Func<Booking, BookingDto>> ToDto = b => new BookingDto(
        b.Id, b.Reference, b.TotalPrice, b.Status,
        b.Seats.OrderBy(s => s.Seat!.RowNumber).ThenBy(s => s.Seat!.SeatNumber)
            .Select(s => new BookingSeatDto(s.Seat!.RowNumber, s.Seat.SeatNumber, s.Seat.Type, s.Price)).ToList(),
        b.Showing!.Movie!.Title, b.Showing.Auditorium!.Name, b.Showing.StartTime, b.CreatedAt, b.CancelledAt);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static BookingDto AsUtc(BookingDto dto) =>
        dto with { Start = Utc(dto.Start), CreatedAt = Utc(dto.CreatedAt), CancelledAt = dto.CancelledAt is { } c ? Utc(c) : null };

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
        var start = Utc(showing.StartTime);
        return new BookingOk(new BookingDto(booking.Id, booking.Reference, booking.TotalPrice, booking.Status,
            lines.Select(l => new BookingSeatDto(l.Seat.RowNumber, l.Seat.SeatNumber, l.Seat.Type, l.Price)).ToList(),
            showing.Movie!.Title, showing.Auditorium!.Name, start, Utc(booking.CreatedAt)));
    }

    /// <summary>
    /// The caller's own bookings. Upcoming: Confirmed and the showing has not started, earliest first. History: every
    /// other booking (started showings, cancelled bookings), latest first. Together they hold each booking once.
    /// </summary>
    public async Task<List<BookingDto>> GetMineAsync(string userId, BookingScope scope, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var mine = db.Bookings.AsNoTracking().Where(b => b.UserId == userId);
        var list = scope == BookingScope.Upcoming
            ? mine.Where(b => b.Status == BookingStatus.Confirmed && b.Showing!.StartTime > now)
                .OrderBy(b => b.Showing!.StartTime).ThenBy(b => b.Id)
            : mine.Where(b => !(b.Status == BookingStatus.Confirmed && b.Showing!.StartTime > now))
                .OrderByDescending(b => b.Showing!.StartTime).ThenByDescending(b => b.Id);
        return (await list.Select(ToDto).ToListAsync(ct)).Select(AsUtc).ToList();
    }

    /// <summary>One booking: Ok for its owner or a caller who may manage bookings, Forbidden for others, NotFound if unknown.</summary>
    public async Task<BookingResult> GetByIdAsync(int id, string userId, bool canManage, CancellationToken ct = default)
    {
        var owner = await db.Bookings.AsNoTracking().Where(b => b.Id == id).Select(b => b.UserId).SingleOrDefaultAsync(ct);
        if (owner is null)
        {
            return new BookingNotFound();
        }
        if (owner != userId && !canManage)
        {
            return new BookingForbidden();
        }
        var dto = await db.Bookings.AsNoTracking().Where(b => b.Id == id).Select(ToDto).SingleAsync(ct);
        return new BookingOk(AsUtc(dto));
    }

    /// <summary>Staff search: by showing and/or exact reference (any case), latest showing first, at most <see cref="MaxSearchResults"/>.</summary>
    public async Task<List<BookingDto>> SearchAsync(int? showingId, string? reference, CancellationToken ct = default)
    {
        var query = db.Bookings.AsNoTracking().AsQueryable();
        if (showingId is { } showing)
        {
            query = query.Where(b => b.ShowingId == showing);
        }
        if (!string.IsNullOrWhiteSpace(reference))
        {
            var normalized = reference.Trim().ToUpperInvariant();
            query = query.Where(b => b.Reference == normalized);
        }
        var list = await query.OrderByDescending(b => b.Showing!.StartTime).ThenByDescending(b => b.Id)
            .Take(MaxSearchResults).Select(ToDto).ToListAsync(ct);
        return list.Select(AsUtc).ToList();
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

    /// <summary>
    /// Cancels a booking for the caller. Order of checks: unknown (404), not the owner and not privileged (403, before
    /// any state is revealed), already cancelled (409), then the time limit for a customer (409). Admin and staff with
    /// ManageBookings are privileged: any booking, any time. One save, then <see cref="BookingCancelled"/> after commit;
    /// a subscriber that throws is logged and never undoes the committed cancellation.
    /// </summary>
    public async Task<BookingResult> CancelAsync(int bookingId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var booking = await db.Bookings.Include(b => b.Seats).Include(b => b.Showing)
            .SingleOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null)
        {
            return new BookingNotFound();
        }

        var privileged = user.IsInRole(Roles.Admin) || user.HasClaim(Permissions.ClaimType, Permissions.ManageBookings);
        if (!privileged && user.FindFirst("sub")?.Value != booking.UserId)
        {
            return new BookingForbidden();
        }
        if (booking.Status == BookingStatus.Cancelled)
        {
            return new BookingAlreadyCancelled();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var limit = options.Value.CancellationLimitHours;
        if (!privileged && now > booking.Showing!.StartTime - TimeSpan.FromHours(limit))
        {
            return new BookingTooLate(limit);
        }

        await CancelInternalAsync(booking, ct);
        booking.CancelledAt = now;
        await db.SaveChangesAsync(ct);

        try
        {
            await events.BookingCancelledAsync(new BookingCancelled(booking.Id, booking.Reference, booking.ShowingId, booking.UserId), ct);
        }
        catch (Exception ex)
        {
            // The cancellation is already committed; a failing subscriber must not turn it into an error.
            logger.LogError(ex, "Handling BookingCancelled for booking {BookingId} failed", booking.Id);
        }
        return new BookingCancelledOk();
    }

    private async Task<BookingResult> ConflictAsync(int showingId, CancellationToken ct)
    {
        var map = await SeatMapBuilder.BuildAsync(db, prices, showingId, clock, ct);
        return map is null ? new BookingShowingClosed() : new BookingConflict(map);
    }
}
