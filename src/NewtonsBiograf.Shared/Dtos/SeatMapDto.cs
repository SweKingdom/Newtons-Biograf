namespace NewtonsBiograf.Shared;

/// <summary>
/// The seats of a showing's auditorium, ordered by Row then Number. Start is UTC. IsBookable is false once the
/// showing has started (the map can still be viewed).
/// </summary>
public record SeatMapDto(int ShowingId, DateTime Start, bool IsBookable, List<SeatDto> Seats);

/// <summary>Price is in whole SEK, from configuration. IsTaken is true while a Confirmed booking holds the seat.</summary>
public record SeatDto(int SeatId, int Row, int Number, SeatType Type, int Price, bool IsTaken);
