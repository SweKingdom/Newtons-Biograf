using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Pricing;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

/// <summary>
/// Anonymous seat map of a showing. A separate controller because <see cref="ShowingsController"/> is staff-only.
/// Contains no user or booking data.
/// </summary>
[ApiController]
[Route("api/showings/{id:int}/seats")]
[AllowAnonymous]
public class ShowingSeatsController(AppDbContext db, IPriceCalculator prices, TimeProvider clock) : ControllerBase
{
    /// <summary>Returns the seat map, also for a showing that has started; answers not found when unknown or cancelled.</summary>
    [HttpGet]
    public async Task<ActionResult<SeatMapDto>> Get(int id, CancellationToken ct)
    {
        var map = await SeatMapBuilder.BuildAsync(db, prices, id, clock, ct);
        return map is null ? NotFound() : map;
    }
}
