using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController(BookingService bookings, IAuthorizationService authorization) : ControllerBase
{
    /// <summary>Books seats for the calling customer (the user id comes from the token, never from the body).</summary>
    [HttpPost]
    [Authorize(Policy = Policies.CustomerOnly)]
    public async Task<IActionResult> Create(CreateBookingRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst("sub")!.Value;
        switch (await bookings.CreateAsync(userId, request, ct))
        {
            case BookingOk ok:
                return Created($"/api/bookings/{ok.Booking.Id}", ok.Booking);
            case BookingInvalid invalid:
                ModelState.AddModelError(nameof(CreateBookingRequest.SeatIds), invalid.Message);
                return ValidationProblem(ModelState);
            case BookingNotFound:
                return NotFound();
            case BookingShowingClosed:
                return Problem(detail: "This showing can no longer be booked.", statusCode: StatusCodes.Status409Conflict);
            case BookingConflict conflict:
                return Conflict(new SeatConflictDto("One or more seats are no longer available", conflict.SeatMap));
            default:
                throw new InvalidOperationException("Unknown booking result.");
        }
    }

    /// <summary>The calling customer's bookings: scope=upcoming (earliest first) or scope=history (latest first).</summary>
    [HttpGet("mine")]
    [Authorize(Policy = Policies.CustomerOnly)]
    public async Task<IActionResult> Mine([FromQuery] string? scope, CancellationToken ct)
    {
        BookingScope parsed;
        switch (scope?.Trim().ToLowerInvariant())
        {
            case "upcoming":
                parsed = BookingScope.Upcoming;
                break;
            case "history":
                parsed = BookingScope.History;
                break;
            default:
                ModelState.AddModelError("scope", "scope is required and must be 'upcoming' or 'history'.");
                return ValidationProblem(ModelState);
        }
        return Ok(await bookings.GetMineAsync(User.FindFirst("sub")!.Value, parsed, ct));
    }

    /// <summary>One booking: its owner, or staff who may manage bookings.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var canManage = (await authorization.AuthorizeAsync(User, Policies.ManageBookings)).Succeeded;
        return await bookings.GetByIdAsync(id, User.FindFirst("sub")!.Value, canManage, ct) switch
        {
            BookingOk ok => Ok(ok.Booking),
            BookingForbidden => Forbid(),
            _ => NotFound()
        };
    }

    /// <summary>Staff search by showing and/or reference; at least one filter is required.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.ManageBookings)]
    public async Task<IActionResult> Search([FromQuery] int? showingId, [FromQuery] string? reference, CancellationToken ct)
    {
        if (showingId is null && string.IsNullOrWhiteSpace(reference))
        {
            ModelState.AddModelError("filter", "Give a showingId, a reference or both.");
            return ValidationProblem(ModelState);
        }
        return Ok(await bookings.SearchAsync(showingId, reference, ct));
    }
}
