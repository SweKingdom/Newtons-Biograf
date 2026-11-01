using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Data;

public class Movie
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public Genre Genre { get; set; }
    public AgeRating AgeRating { get; set; }
    public int LengthMinutes { get; set; }
    public string? Description { get; set; }
}

public class Auditorium
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Rows { get; set; }
    public int SeatsPerRow { get; set; }
    public List<Seat> Seats { get; set; } = [];
}

public class Seat
{
    public int Id { get; set; }
    public int AuditoriumId { get; set; }
    public Auditorium? Auditorium { get; set; }
    public int RowNumber { get; set; }
    public int SeatNumber { get; set; }
    public SeatType Type { get; set; }
}

/// <summary>StartTime and EndTime are UTC.</summary>
public class Showing
{
    public int Id { get; set; }
    public int MovieId { get; set; }
    public Movie? Movie { get; set; }
    public int AuditoriumId { get; set; }
    public Auditorium? Auditorium { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public ShowingStatus Status { get; set; } = ShowingStatus.Scheduled;
}

/// <summary>CreatedAt and CancelledAt are UTC.</summary>
public class Booking
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public int ShowingId { get; set; }
    public Showing? Showing { get; set; }
    public decimal TotalPrice { get; set; }
    public BookingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public List<BookingSeat> Seats { get; set; } = [];
}

/// <summary>Rows are kept on cancel; IsActive = false frees the seat for the showing.</summary>
public class BookingSeat
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking? Booking { get; set; }
    public int ShowingId { get; set; }
    public int SeatId { get; set; }
    public Seat? Seat { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}
