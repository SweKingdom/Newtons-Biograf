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
