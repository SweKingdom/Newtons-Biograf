using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Services;

/// <summary>Bound from the "Showings" configuration section (validated on start).</summary>
public class ShowingOptions
{
    public const string Section = "Showings";

    public int CleaningMinutes { get; set; } = 15;
    public string DisplayTimeZone { get; set; } = "Europe/Stockholm";
}

public abstract record SlotResult;

/// <summary>The slot is valid: UTC start and end plus the loaded movie and auditorium.</summary>
public sealed record SlotOk(DateTime Start, DateTime End, Movie Movie, Auditorium Auditorium) : SlotResult;

/// <summary>Validation errors keyed by request field (400).</summary>
public sealed record SlotInvalid(Dictionary<string, string[]> Errors) : SlotResult;

/// <summary>The slot overlaps another showing in the auditorium (409).</summary>
public sealed record SlotConflict(string Detail) : SlotResult;

/// <summary>Slot rules shared by creating and editing a showing, and cancelling a showing with its bookings.</summary>
public class ShowingService(IRepository<Movie> movies, IRepository<Auditorium> auditoriums,
    IRepository<Showing> showings, IOptions<ShowingOptions> options, AppDbContext db, BookingService bookingService,
    IBookingEvents events, ILogger<ShowingService> logger)
{
    /// <summary>
    /// Checks the start (UTC offset given, after now), the movie and auditorium, and overlaps with other non-cancelled
    /// showings in the auditorium. <paramref name="excludeId"/> is the showing being edited, if any.
    /// </summary>
    public async Task<SlotResult> ValidateSlotAsync(ShowingRequest request, int? excludeId, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();

        DateTime start = default;
        if (request.StartTime.Kind == DateTimeKind.Unspecified)
        {
            errors[nameof(ShowingRequest.StartTime)] =
                ["StartTime is required and must include a UTC offset, for example 2030-01-15T18:00:00Z"];
        }
        else
        {
            start = request.StartTime.ToUniversalTime();
            if (start <= DateTime.UtcNow)
            {
                errors[nameof(ShowingRequest.StartTime)] = ["StartTime must be in the future"];
            }
        }

        var movie = await movies.GetByIdAsync(request.MovieId, ct);
        if (movie is null)
        {
            errors[nameof(ShowingRequest.MovieId)] = ["The movie does not exist"];
        }
        var auditorium = await auditoriums.GetByIdAsync(request.AuditoriumId, ct);
        if (auditorium is null)
        {
            errors[nameof(ShowingRequest.AuditoriumId)] = ["The auditorium does not exist"];
        }
        if (errors.Count > 0)
        {
            return new SlotInvalid(errors);
        }

        var end = start.AddMinutes(movie!.LengthMinutes + options.Value.CleaningMinutes);
        var excluded = excludeId ?? 0;
        var clash = await showings.Query().AsNoTracking()
            .Include(s => s.Movie).Include(s => s.Auditorium)
            .Where(s => s.AuditoriumId == request.AuditoriumId && s.Status != ShowingStatus.Cancelled
                && s.Id != excluded && s.StartTime < end && start < s.EndTime)
            .OrderBy(s => s.StartTime)
            .FirstOrDefaultAsync(ct);
        if (clash is not null)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.DisplayTimeZone);
            return new SlotConflict(
                $"Overlaps with '{clash.Movie!.Title}' {Local(clash.StartTime, zone)}–{Local(clash.EndTime, zone)} in {clash.Auditorium!.Name}");
        }

        return new SlotOk(start, end, movie, auditorium!);
    }

    /// <summary>
    /// Cancels the showing and every Confirmed booking in one transaction, then raises one BookingCancelled per
    /// cancelled booking after commit. Returns false when the showing does not exist; an already cancelled showing
    /// changes nothing. Any failure inside the transaction rolls everything back and is rethrown.
    /// </summary>
    public async Task<bool> CancelShowingAsync(int id, CancellationToken ct = default)
    {
        List<BookingCancelled> cancelled = [];
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var showing = await db.Showings.SingleOrDefaultAsync(s => s.Id == id, ct);
            if (showing is null)
            {
                return false;
            }
            if (showing.Status == ShowingStatus.Cancelled)
            {
                return true;
            }

            showing.Status = ShowingStatus.Cancelled;
            var confirmed = await db.Bookings.Include(b => b.Seats)
                .Where(b => b.ShowingId == id && b.Status == BookingStatus.Confirmed).ToListAsync(ct);
            foreach (var booking in confirmed)
            {
                await bookingService.CancelInternalAsync(booking, ct);
                cancelled.Add(new BookingCancelled(booking.Id, booking.Reference, id, booking.UserId));
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        foreach (var e in cancelled)
        {
            try
            {
                await events.BookingCancelledAsync(e, ct);
            }
            catch (Exception ex)
            {
                // The cancellation is already committed; a failing subscriber must not turn it into an error.
                logger.LogError(ex, "Handling BookingCancelled for booking {BookingId} failed", e.BookingId);
            }
        }
        return true;
    }

    private static string Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
            .ToString("HH:mm", CultureInfo.InvariantCulture);
}
