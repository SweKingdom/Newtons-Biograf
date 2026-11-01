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
    AppDbContext db,
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

    /// <summary>Replaces the full permission set of a staff user. Takes effect on the user's next login.</summary>
    [HttpPut("{id}/permissions")]
    public async Task<IActionResult> UpdatePermissions(string id, UpdatePermissionsRequest request)
    {
        var requested = request.Permissions!.Distinct().ToArray();
        var unknown = requested.Where(p => !Permissions.All.Contains(p)).ToArray();
        if (unknown.Length > 0)
        {
            foreach (var name in unknown)
            {
                ModelState.AddModelError(nameof(UpdatePermissionsRequest.Permissions), $"Unknown permission: {name}.");
            }
            return ValidationProblem(ModelState);
        }

        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            return Problem(title: "Staff user not found.", statusCode: StatusCodes.Status404NotFound);
        }
        if (!await users.IsInRoleAsync(user, Roles.Staff))
        {
            return Problem(title: "Permissions can only be changed for staff accounts.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var current = (await users.GetClaimsAsync(user)).Where(c => c.Type == Permissions.ClaimType).ToArray();
        var currentNames = current.Select(c => c.Value).ToArray();
        var toRemove = current.Where(c => !requested.Contains(c.Value)).ToArray();
        var toAdd = requested.Except(currentNames).Select(p => new Claim(Permissions.ClaimType, p)).ToArray();

        if (toRemove.Length > 0 || toAdd.Length > 0)
        {
            // One transaction so a failed add never leaves the user with the old set half removed.
            await using var transaction = await db.Database.BeginTransactionAsync();
            var removed = toRemove.Length == 0 ? IdentityResult.Success : await users.RemoveClaimsAsync(user, toRemove);
            var added = !removed.Succeeded || toAdd.Length == 0 ? removed : await users.AddClaimsAsync(user, toAdd);
            if (!added.Succeeded)
            {
                logger.LogError("Could not update permissions of staff user {UserId}: {Failure}.",
                    user.Id, string.Join(", ", added.Errors.Select(e => e.Code)));
                return Problem(title: "Could not update the permissions. Please try again later.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
            await transaction.CommitAsync();
        }

        return NoContent();
    }

    private static string FieldFor(string code) => code switch
    {
        _ when code.StartsWith("Password") => nameof(CreateStaffRequest.Password),
        _ when code.Contains("Email") || code.Contains("UserName") => nameof(CreateStaffRequest.Email),
        _ => ""
    };
}
