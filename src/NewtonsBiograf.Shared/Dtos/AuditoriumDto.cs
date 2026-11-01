using System.ComponentModel.DataAnnotations;

namespace NewtonsBiograf.Shared;

/// <summary>Payload for creating (POST) and editing (PUT) an auditorium. Capacity is derived, never sent.</summary>
public record AuditoriumRequest
{
    [Required(ErrorMessage = "Name is required"),
     StringLength(100, ErrorMessage = "Name must be at most 100 characters")]
    public string Name { get; init; } = "";

    [Range(1, 26, ErrorMessage = "Rows must be a whole number between 1 and 26")]
    public int Rows { get; init; }

    [Range(1, 40, ErrorMessage = "Seats per row must be a whole number between 1 and 40")]
    public int SeatsPerRow { get; init; }
}

/// <summary>Capacity = Rows × SeatsPerRow. VipRows are the row numbers whose seats are all VIP, ascending.</summary>
public record AuditoriumDto(int Id, string Name, int Rows, int SeatsPerRow, int Capacity, int[] VipRows);
