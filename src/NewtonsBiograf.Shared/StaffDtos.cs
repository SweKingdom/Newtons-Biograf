using System.ComponentModel.DataAnnotations;

namespace NewtonsBiograf.Shared;

/// <summary>Payload an admin sends to create a staff account. Permission names are validated by the API.</summary>
public record CreateStaffRequest
{
    [Required(ErrorMessage = "Enter the staff member's name."),
     StringLength(100, ErrorMessage = "Name can be at most 100 characters.")]
    public string Name { get; init; } = "";

    [Required(ErrorMessage = "Enter an email address."),
     EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; init; } = "";

    [Required(ErrorMessage = "Enter a password."),
     MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    public string Password { get; init; } = "";

    public string[]? Permissions { get; init; } = [];
}

public record StaffDto(string Id, string Name, string Email, string[] Permissions);
