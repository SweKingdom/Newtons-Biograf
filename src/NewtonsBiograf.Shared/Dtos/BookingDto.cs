namespace NewtonsBiograf.Shared;

/// <summary>Limits shared by the API validation and the booking screens.</summary>
public static class BookingLimits
{
    public const int MaxSeats = 8;
}

/// <summary>Book 1–8 distinct seats of the showing's auditorium.</summary>
public record CreateBookingRequest(int ShowingId, List<int> SeatIds);

/// <summary>Price is the seat price at booking time, in SEK.</summary>
public record BookingSeatDto(int Row, int Number, SeatType Type, decimal Price);

/// <summary>Which of a customer's bookings to list: Confirmed ones on showings that have not started, or all others.</summary>
public enum BookingScope
{
    Upcoming,
    History
}

/// <summary>A booking. Reference is the 8-character code shown to the customer; Start, CreatedAt and CancelledAt are UTC.</summary>
public record BookingDto(int Id, string Reference, decimal TotalPrice, BookingStatus Status, List<BookingSeatDto> Seats,
    string MovieTitle, string Auditorium, DateTime Start, DateTime CreatedAt, DateTime? CancelledAt = null);

/// <summary>Body of a 409 when seats were taken by someone else; SeatMap is the refreshed map of the showing.</summary>
public record SeatConflictDto(string Message, SeatMapDto SeatMap);
