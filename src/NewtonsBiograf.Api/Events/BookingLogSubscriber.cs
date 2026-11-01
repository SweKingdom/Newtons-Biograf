namespace NewtonsBiograf.Api.Events;

/// <summary>Logs booking events (ids only, no personal data). Subscribes when constructed; resolved once at startup.</summary>
public sealed class BookingLogSubscriber
{
    private readonly ILogger<BookingLogSubscriber> _logger;

    public BookingLogSubscriber(IBookingEventPublisher publisher, ILogger<BookingLogSubscriber> logger)
    {
        _logger = logger;
        publisher.BookingCreated += OnBookingCreated;
    }

    private void OnBookingCreated(object? sender, BookingEventArgs e) =>
        _logger.LogInformation("Booking {BookingId} created by user {UserId} for showing {ShowingId}",
            e.BookingId, e.UserId, e.ShowingId);
}
