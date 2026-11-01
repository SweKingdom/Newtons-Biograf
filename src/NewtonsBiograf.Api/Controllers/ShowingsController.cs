using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

[ApiController]
[Route("api/showings")]
[Authorize(Policy = Policies.ManageShowings)]
public class ShowingsController(ShowingService slots, IRepository<Showing> showings, IRepository<Booking> bookings)
    : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ShowingDto>> Get(int id, CancellationToken ct)
    {
        var showing = await showings.Query().AsNoTracking()
            .Include(s => s.Movie).Include(s => s.Auditorium)
            .SingleOrDefaultAsync(s => s.Id == id, ct);
        if (showing is null)
        {
            return NotFound();
        }

        var active = await bookings.Query().CountAsync(
            b => b.ShowingId == id && b.Status == BookingStatus.Confirmed, ct);
        return ToDto(showing, active);
    }

    /// <summary>EndTime = StartTime + movie length + cleaning minutes; rejected when in the past or overlapping.</summary>
    [HttpPost]
    public async Task<ActionResult<ShowingDto>> Create(ShowingRequest request, CancellationToken ct)
    {
        switch (await slots.ValidateSlotAsync(request, null, ct))
        {
            case SlotInvalid invalid:
                return ValidationProblem(new ValidationProblemDetails(invalid.Errors));
            case SlotConflict conflict:
                return Problem(detail: conflict.Detail, statusCode: StatusCodes.Status409Conflict);
            case SlotOk ok:
                var showing = new Showing
                {
                    MovieId = ok.Movie.Id,
                    Movie = ok.Movie,
                    AuditoriumId = ok.Auditorium.Id,
                    Auditorium = ok.Auditorium,
                    StartTime = ok.Start,
                    EndTime = ok.End,
                    Status = ShowingStatus.Scheduled
                };
                await showings.AddAsync(showing, ct);
                await showings.SaveChangesAsync(ct);
                return CreatedAtAction(nameof(Get), new { id = showing.Id }, ToDto(showing, 0));
            default:
                throw new InvalidOperationException("Unexpected slot validation result.");
        }
    }

    private static ShowingDto ToDto(Showing s, int activeBookingCount) =>
        new(s.Id, s.MovieId, s.Movie!.Title, s.AuditoriumId, s.Auditorium!.Name,
            DateTime.SpecifyKind(s.StartTime, DateTimeKind.Utc), DateTime.SpecifyKind(s.EndTime, DateTimeKind.Utc),
            s.Status, activeBookingCount);
}
