using System.ComponentModel.DataAnnotations;

namespace NewtonsBiograf.Shared;

/// <summary>Payload for creating (POST) and editing (PUT) a movie. Genre and AgeRating are nullable so a missing value fails validation.</summary>
public record MovieRequest
{
    [Required(ErrorMessage = "Title is required"),
     StringLength(200, ErrorMessage = "Title must be at most 200 characters")]
    public string Title { get; init; } = "";

    [Required(ErrorMessage = "Genre is required"),
     EnumDataType(typeof(Genre), ErrorMessage = "Genre is not a valid genre")]
    public Genre? Genre { get; init; }

    [Required(ErrorMessage = "Age rating is required"),
     EnumDataType(typeof(AgeRating), ErrorMessage = "Age rating is not a valid age rating")]
    public AgeRating? AgeRating { get; init; }

    [Range(1, 400, ErrorMessage = "Length must be a whole number between 1 and 400")]
    public int LengthMinutes { get; init; }

    [StringLength(2000, ErrorMessage = "Description must be at most 2000 characters")]
    public string? Description { get; init; }
}

public record MovieDto(int Id, string Title, Genre Genre, AgeRating AgeRating, int LengthMinutes, string? Description);
