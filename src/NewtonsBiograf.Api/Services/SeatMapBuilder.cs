using Microsoft.EntityFrameworkCore;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Pricing;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Services;

/// <summary>Builds the seat map of a showing; also used for the 409 body when a booking fails.</summary>
public static class SeatMapBuilder
{
    /// <summary>
    /// Returns null when the showing does not exist or is cancelled. A seat is taken while an active booking seat
    /// of this showing holds it (IsActive is cleared on cancel). IsBookable is Scheduled and starting after now.
    /// </summary>
    public static async Task<SeatMapDto?> BuildAsync(AppDbContext db, IPriceCalculator prices, int showingId,
        TimeProvider clock, CancellationToken ct = default)
    {
        var showing = await db.Showings.AsNoTracking()
            .Where(s => s.Id == showingId)
            .Select(s => new { s.AuditoriumId, s.StartTime, s.Status })
            .SingleOrDefaultAsync(ct);
        if (showing is null || showing.Status == ShowingStatus.Cancelled)
        {
            return null;
        }

        var takenIds = db.BookingSeats.Where(bs => bs.ShowingId == showingId && bs.IsActive).Select(bs => bs.SeatId);
        var rows = await db.Seats.AsNoTracking()
            .Where(s => s.AuditoriumId == showing.AuditoriumId)
            .OrderBy(s => s.RowNumber).ThenBy(s => s.SeatNumber)
            .Select(s => new { s.Id, s.RowNumber, s.SeatNumber, s.Type, IsTaken = takenIds.Contains(s.Id) })
            .ToListAsync(ct);

        var start = DateTime.SpecifyKind(showing.StartTime, DateTimeKind.Utc);
        var seats = rows
            .Select(r => new SeatDto(r.Id, r.RowNumber, r.SeatNumber, r.Type, (int)prices.GetPrice(r.Type), r.IsTaken))
            .ToList();
        return new SeatMapDto(showingId, start, showing.Status == ShowingStatus.Scheduled && start > clock.GetUtcNow().UtcDateTime, seats);
    }
}
