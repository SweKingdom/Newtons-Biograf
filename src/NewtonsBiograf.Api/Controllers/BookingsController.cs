using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController(BookingService bookings) : ControllerBase
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
}
