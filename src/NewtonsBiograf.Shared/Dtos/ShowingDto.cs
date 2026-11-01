namespace NewtonsBiograf.Shared;

/// <summary>Payload for scheduling a showing. StartTime is ISO-8601 with "Z" or an offset; EndTime is derived.</summary>
public record ShowingRequest
{
    public int MovieId { get; init; }

    public int AuditoriumId { get; init; }

    public DateTime StartTime { get; init; }
}

/// <summary>StartTime and EndTime are UTC. ActiveBookingCount is the number of confirmed bookings.</summary>
public record ShowingDto(
    int Id,
    int MovieId,
    string MovieTitle,
    int AuditoriumId,
    string AuditoriumName,
    DateTime StartTime,
    DateTime EndTime,
    ShowingStatus Status,
    int ActiveBookingCount);

/// <summary>Scheduling settings the screens need: minutes added after each film for cleaning.</summary>
public record ShowingSettingsDto(int CleaningMinutes);

/// <summary>
/// A showing in the public list. Genre and AgeRating are display names ("Sci-Fi", "All ages"); Start and End are UTC;
/// FreeSeats is the auditorium's seat count minus the seats of Confirmed bookings.
/// </summary>
public record ShowingListItem(
    int Id,
    int MovieId,
    string Movie,
    string Genre,
    string AgeRating,
    int LengthMinutes,
    int AuditoriumId,
    string Auditorium,
    DateTime Start,
    DateTime End,
    int FreeSeats);

/// <summary>An auditorium as offered in the public filter dropdown.</summary>
public record AuditoriumOption(int Id, string Name);
