using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

[ApiController]
[Route("api/showings")]
[Authorize(Policy = Policies.ManageShowings)]
public class ShowingsController(ShowingService slots, IRepository<Showing> showings, IRepository<Booking> bookings,
    IOptions<ShowingOptions> options)
    : ControllerBase
{
    /// <summary>Scheduling settings (cleaning minutes, display time zone) for the screens; not secret, so anonymous.</summary>
    [HttpGet("settings")]
    [AllowAnonymous]
    public ShowingSettingsDto Settings() => new(options.Value.CleaningMinutes, options.Value.DisplayTimeZone);

    /// <summary>Staff list of showings starting in [from, to), optionally in one auditorium, ordered by start time.</summary>
    [HttpGet]
    public async Task<ActionResult<ShowingDto[]>> List(
        DateTime? from, DateTime? to, int? auditoriumId, CancellationToken ct)
    {
        if (from is null)
        {
            ModelState.AddModelError(nameof(from), "The query parameter 'from' is required.");
        }
        if (to is null)
        {
            ModelState.AddModelError(nameof(to), "The query parameter 'to' is required.");
        }
        if (from is not null && to is not null && ToUtc(to.Value) <= ToUtc(from.Value))
        {
            ModelState.AddModelError(nameof(to), "'to' must be after 'from'.");
        }
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var start = ToUtc(from!.Value);
        var end = ToUtc(to!.Value);
        var query = showings.Query().AsNoTracking().Include(s => s.Movie).Include(s => s.Auditorium)
            .Where(s => s.StartTime >= start && s.StartTime < end);
        if (auditoriumId is not null)
        {
            query = query.Where(s => s.AuditoriumId == auditoriumId);
        }

        var found = await query.OrderBy(s => s.StartTime).ThenBy(s => s.Id).ToListAsync(ct);
        var ids = found.Select(s => s.Id).ToList();
        var counts = await bookings.Query()
            .Where(b => ids.Contains(b.ShowingId) && b.Status == BookingStatus.Confirmed)
            .GroupBy(b => b.ShowingId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return found.Select(s => ToDto(s, counts.GetValueOrDefault(s.Id))).ToArray();
    }

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

    /// <summary>
    /// Only a scheduled showing that has not started and has no Confirmed bookings can be edited; the start,
    /// movie, auditorium and overlap rules apply again (the showing itself is ignored).
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ShowingRequest request, CancellationToken ct)
    {
        var showing = await showings.GetByIdAsync(id, ct);
        if (showing is null)
        {
            return NotFound();
        }
        if (showing.Status == ShowingStatus.Cancelled)
        {
            return Problem(detail: "Cancelled showings cannot be edited.", statusCode: StatusCodes.Status409Conflict);
        }
        if (showing.StartTime <= DateTime.UtcNow
            || await bookings.Query().AnyAsync(b => b.ShowingId == id && b.Status == BookingStatus.Confirmed, ct))
        {
            return Problem(
                detail: "Showing has bookings or has started and cannot be edited. Cancel it and create a new one.",
                statusCode: StatusCodes.Status409Conflict);
        }

        switch (await slots.ValidateSlotAsync(request, id, ct))
        {
            case SlotInvalid invalid:
                return ValidationProblem(new ValidationProblemDetails(invalid.Errors));
            case SlotConflict conflict:
                return Problem(detail: conflict.Detail, statusCode: StatusCodes.Status409Conflict);
            case SlotOk ok:
                showing.MovieId = ok.Movie.Id;
                showing.AuditoriumId = ok.Auditorium.Id;
                showing.StartTime = ok.Start;
                showing.EndTime = ok.End;
                await showings.SaveChangesAsync(ct);
                return NoContent();
            default:
                throw new InvalidOperationException("Unexpected slot validation result.");
        }
    }

    /// <summary>Cancels the showing and all its Confirmed bookings; idempotent; allowed for started showings.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        await slots.CancelShowingAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>A showing with any booking (even cancelled) is kept for history and can only be cancelled.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var showing = await showings.GetByIdAsync(id, ct);
        if (showing is null)
        {
            return NotFound();
        }
        if (await bookings.Query().AnyAsync(b => b.ShowingId == id, ct))
        {
            return Problem(detail: "Showing has bookings and can only be cancelled.",
                statusCode: StatusCodes.Status409Conflict);
        }

        showings.Remove(showing);
        await showings.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Query values without an offset are taken as UTC.</summary>
    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

    private static ShowingDto ToDto(Showing s, int activeBookingCount) =>
        new(s.Id, s.MovieId, s.Movie!.Title, s.AuditoriumId, s.Auditorium!.Name,
            DateTime.SpecifyKind(s.StartTime, DateTimeKind.Utc), DateTime.SpecifyKind(s.EndTime, DateTimeKind.Utc),
            s.Status, activeBookingCount);
}
