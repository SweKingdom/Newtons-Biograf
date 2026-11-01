using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

[ApiController]
[Route("api/admin/staff")]
[Authorize(Policy = Policies.AdminOnly)]
public class AdminStaffController(
    UserManager<ApplicationUser> users,
    ILogger<AdminStaffController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateStaffRequest request)
    {
        // Case-sensitive exact match; duplicates are collapsed. Nothing is created when any name is unknown.
        var permissions = (request.Permissions ?? []).Distinct().ToArray();
        var unknown = permissions.Where(p => !Permissions.All.Contains(p)).ToArray();
        if (unknown.Length > 0)
        {
            foreach (var name in unknown)
            {
                ModelState.AddModelError(nameof(CreateStaffRequest.Permissions), $"Unknown permission: {name}.");
            }
            return ValidationProblem(ModelState);
        }

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

        string? failure = null;
        try
        {
            var assigned = await users.AddToRoleAsync(user, Roles.Staff);
            if (!assigned.Succeeded)
            {
                failure = string.Join(", ", assigned.Errors.Select(e => e.Code));
            }
            else if (permissions.Length > 0)
            {
                var claimed = await users.AddClaimsAsync(user, permissions.Select(p => new Claim(Permissions.ClaimType, p)));
                if (!claimed.Succeeded)
                {
                    failure = string.Join(", ", claimed.Errors.Select(e => e.Code));
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            failure = ex.Message; // thrown by Identity when the role does not exist
        }

        if (failure is not null)
        {
            logger.LogError("Could not finish creating staff user {UserId}: {Failure}. The user was removed.", user.Id, failure);
            await users.DeleteAsync(user);
            return Problem(title: "Could not create the staff account. Please try again later.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Created("/api/admin/staff", new StaffDto(user.Id, user.DisplayName, user.Email!, permissions));
    }

    [HttpGet]
    public async Task<ActionResult<StaffDto[]>> List()
    {
        var staff = new List<StaffDto>();
        foreach (var user in (await users.GetUsersInRoleAsync(Roles.Staff)).OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var permissions = (await users.GetClaimsAsync(user))
                .Where(c => c.Type == Permissions.ClaimType)
                .Select(c => c.Value)
                .ToArray();
            staff.Add(new StaffDto(user.Id, user.DisplayName, user.Email!, permissions));
        }
        return Ok(staff);
    }

    private static string FieldFor(string code) => code switch
    {
        _ when code.StartsWith("Password") => nameof(CreateStaffRequest.Password),
        _ when code.Contains("Email") || code.Contains("UserName") => nameof(CreateStaffRequest.Email),
        _ => ""
    };
}
