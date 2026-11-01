namespace NewtonsBiograf.Api.Events;

public sealed class BookingEventArgs(int bookingId, string userId, int showingId) : EventArgs
{
    public int BookingId { get; } = bookingId;
    public string UserId { get; } = userId;
    public int ShowingId { get; } = showingId;
}

public delegate void BookingEventHandler(object? sender, BookingEventArgs e);

/// <summary>Raised synchronously after a booking change has been committed; handlers must stay fast.</summary>
public interface IBookingEventPublisher
{
    event BookingEventHandler BookingCreated;

    /// <summary>Calls every subscriber; a subscriber that throws is logged and never fails the caller.</summary>
    void RaiseCreated(int bookingId, string userId, int showingId);
}

public sealed class BookingEventPublisher(ILogger<BookingEventPublisher> logger) : IBookingEventPublisher
{
    public event BookingEventHandler? BookingCreated;

    public void RaiseCreated(int bookingId, string userId, int showingId)
    {
        var args = new BookingEventArgs(bookingId, userId, showingId);
        foreach (var handler in BookingCreated?.GetInvocationList() ?? [])
        {
            try
            {
                ((BookingEventHandler)handler)(this, args);
            }
            catch (Exception ex)
            {
                // The booking is already committed; a failing subscriber must not turn it into an error.
                logger.LogError(ex, "Handling BookingCreated for booking {BookingId} failed", bookingId);
            }
        }
    }
}
