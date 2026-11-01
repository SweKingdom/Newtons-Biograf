using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

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

public class BookingService
{
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
}
