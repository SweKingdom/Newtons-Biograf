using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> users,
    TokenService tokens,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string CustomerRole = "Customer";
    private const string InvalidCredentials = "Invalid email or password.";

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new ApplicationUser { UserName = request.Email, Email = request.Email, DisplayName = request.Name };

        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors)
            {
                ModelState.AddModelError(FieldFor(error.Code), error.Description);
            }
            return ValidationProblem(ModelState);
        }

        // The role is fixed: nothing in the payload can influence it.
        string? failure = null;
        try
        {
            var assigned = await users.AddToRoleAsync(user, CustomerRole);
            if (!assigned.Succeeded)
            {
                failure = string.Join(", ", assigned.Errors.Select(e => e.Code));
            }
        }
        catch (InvalidOperationException ex)
        {
            failure = ex.Message; // thrown by Identity when the role does not exist
        }

        if (failure is not null)
        {
            logger.LogError("Could not assign role {Role} to new user {UserId}: {Failure}. The user was removed.",
                CustomerRole, user.Id, failure);
            await users.DeleteAsync(user);
            return Problem(title: "Registration failed. Please try again later.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return StatusCode(StatusCodes.Status201Created, new RegisterResponse(user.Id, user.DisplayName, user.Email!));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
        {
            return Problem(title: InvalidCredentials, statusCode: StatusCodes.Status401Unauthorized);
        }

        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? "";
        var permissions = (await users.GetClaimsAsync(user))
            .Where(c => c.Type == TokenService.PermissionClaimType)
            .Select(c => c.Value)
            .ToArray();
        var (token, expiresAt) = tokens.Create(user, role, permissions);

        return Ok(new LoginResponse(token, expiresAt, user.DisplayName, role, permissions));
    }

    private static string FieldFor(string code) => code switch
    {
        _ when code.StartsWith("Password") => nameof(RegisterRequest.Password),
        _ when code.Contains("Email") || code.Contains("UserName") => nameof(RegisterRequest.Email),
        _ => ""
    };
}
