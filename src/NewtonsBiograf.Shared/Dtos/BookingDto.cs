namespace NewtonsBiograf.Shared;

/// <summary>Book 1–8 distinct seats of the showing's auditorium.</summary>
public record CreateBookingRequest(int ShowingId, List<int> SeatIds);

/// <summary>Price is the seat price at booking time, in SEK.</summary>
public record BookingSeatDto(int Row, int Number, SeatType Type, decimal Price);

/// <summary>A booking. Reference is the 8-character code shown to the customer; Start is UTC.</summary>
public record BookingDto(int Id, string Reference, decimal TotalPrice, BookingStatus Status, List<BookingSeatDto> Seats,
    string MovieTitle, string Auditorium, DateTime Start);

/// <summary>Body of a 409 when seats were taken by someone else; SeatMap is the refreshed map of the showing.</summary>
public record SeatConflictDto(string Message, SeatMapDto SeatMap);
