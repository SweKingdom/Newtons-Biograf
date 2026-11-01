using System.ComponentModel.DataAnnotations;

namespace NewtonsBiograf.Shared;

/// <summary>Self-registration payload. It deliberately has no role or permission members.</summary>
public record RegisterRequest
{
    [Required(ErrorMessage = "Enter your name."),
     StringLength(100, ErrorMessage = "Name can be at most 100 characters.")]
    public string Name { get; init; } = "";

    [Required(ErrorMessage = "Enter your email address."),
     EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; init; } = "";

    [Required(ErrorMessage = "Enter a password."),
     MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    public string Password { get; init; } = "";
}

public record LoginRequest
{
    [Required(ErrorMessage = "Enter your email address.")]
    public string Email { get; init; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    public string Password { get; init; } = "";
}

public record RegisterResponse(string Id, string Name, string Email);

public record LoginResponse(string Token, DateTime ExpiresAt, string Name, string Role, string[] Permissions);
