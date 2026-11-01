using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

[ApiController]
[Route("api/auditoriums")]
public class AuditoriumsController(IRepository<Auditorium> auditoriums, IRepository<Seat> seats,
    IRepository<BookingSeat> bookingSeats) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<AuditoriumDto[]>> List(CancellationToken ct)
    {
        var all = await auditoriums.Query().AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);
        var vip = await VipRows(null, ct);
        return all.Select(a => ToDto(a, vip[a.Id].ToArray())).ToArray();
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<AuditoriumDto>> Get(int id, CancellationToken ct)
    {
        var auditorium = await auditoriums.GetByIdAsync(id, ct);
        return auditorium is null ? NotFound() : ToDto(auditorium, (await VipRows(id, ct))[id].ToArray());
    }

    /// <summary>Creates the auditorium and its Standard seats in one save.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<AuditoriumDto>> Create(AuditoriumRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (await NameTaken(name, 0, ct))
        {
            return NameTakenProblem();
        }

        var auditorium = new Auditorium
        {
            Name = name,
            Rows = request.Rows,
            SeatsPerRow = request.SeatsPerRow,
            Seats = Positions(request.Rows, request.SeatsPerRow)
                .Select(p => new Seat { RowNumber = p.Row, SeatNumber = p.Number, Type = SeatType.Standard })
                .ToList()
        };
        await auditoriums.AddAsync(auditorium, ct);
        if (!await TrySave(ct))
        {
            return NameTakenProblem();
        }
        return CreatedAtAction(nameof(Get), new { id = auditorium.Id }, ToDto(auditorium, []));
    }

    /// <summary>
    /// The route id wins. A layout change is blocked (409) by bookings; otherwise seats at unchanged positions keep
    /// their id, missing seats are added with their row's type and extra seats are removed.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Update(int id, AuditoriumRequest request, CancellationToken ct)
    {
        var auditorium = await auditoriums.GetByIdAsync(id, ct);
        if (auditorium is null)
        {
            return NotFound();
        }

        var name = request.Name.Trim();
        if (await NameTaken(name, id, ct))
        {
            return NameTakenProblem();
        }

        var rows = request.Rows;
        var perRow = request.SeatsPerRow;
        var layoutChanged = auditorium.Rows != rows || auditorium.SeatsPerRow != perRow;
        if (layoutChanged && await HasBookings(id, rows, perRow, ct))
        {
            return Problem(detail: "Layout cannot be changed because the auditorium has bookings.",
                statusCode: StatusCodes.Status409Conflict);
        }

        auditorium.Name = name;
        if (layoutChanged)
        {
            var existing = await seats.Query().Where(s => s.AuditoriumId == id).ToListAsync(ct);
            foreach (var seat in existing.Where(s => s.RowNumber > rows || s.SeatNumber > perRow))
            {
                seats.Remove(seat);
            }

            var present = existing.Select(s => (s.RowNumber, s.SeatNumber)).ToHashSet();
            var vipRows = existing.GroupBy(s => s.RowNumber)
                .Where(g => g.All(s => s.Type == SeatType.VIP))
                .Select(g => g.Key)
                .ToHashSet();
            foreach (var (row, number) in Positions(rows, perRow).Where(p => !present.Contains(p)))
            {
                await seats.AddAsync(new Seat
                {
                    AuditoriumId = id,
                    RowNumber = row,
                    SeatNumber = number,
                    Type = vipRows.Contains(row) ? SeatType.VIP : SeatType.Standard
                }, ct);
            }
            auditorium.Rows = rows;
            auditorium.SeatsPerRow = perRow;
        }

        return await TrySave(ct) ? NoContent() : NameTakenProblem();
    }

    private static IEnumerable<(int Row, int Number)> Positions(int rows, int perRow) =>
        from row in Enumerable.Range(1, rows)
        from number in Enumerable.Range(1, perRow)
        select (row, number);

    /// <summary>Case-insensitive, done in memory because SQLite's lower() only folds ASCII and the unique index is case-sensitive.</summary>
    private async Task<bool> NameTaken(string name, int exceptId, CancellationToken ct)
    {
        var names = await auditoriums.Query().AsNoTracking().Where(a => a.Id != exceptId).Select(a => a.Name).ToListAsync(ct);
        return names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    private ActionResult NameTakenProblem()
    {
        ModelState.AddModelError(nameof(AuditoriumRequest.Name), "An auditorium with this name already exists");
        return ValidationProblem(ModelState);
    }

    /// <summary>False when the unique Name index rejected the save (two requests raced past the name check).</summary>
    private async Task<bool> TrySave(CancellationToken ct)
    {
        try
        {
            await auditoriums.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Auditorium.Name") == true)
        {
            return false;
        }
    }

    /// <summary>An active booking in a future non-cancelled showing, or any booking of a seat the new layout would remove.</summary>
    private async Task<bool> HasBookings(int id, int rows, int perRow, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await bookingSeats.Query().AnyAsync(bs => bs.IsActive
                   && bs.Booking!.Showing!.AuditoriumId == id
                   && bs.Booking.Showing.StartTime > now
                   && bs.Booking.Showing.Status != ShowingStatus.Cancelled, ct)
               || await bookingSeats.Query().AnyAsync(bs => bs.Seat!.AuditoriumId == id
                   && (bs.Seat.RowNumber > rows || bs.Seat.SeatNumber > perRow), ct);
    }

    /// <summary>Row numbers whose seats are all VIP, keyed by auditorium id (one grouped query).</summary>
    private async Task<ILookup<int, int>> VipRows(int? auditoriumId, CancellationToken ct)
    {
        var rows = await seats.Query().AsNoTracking()
            .Where(s => auditoriumId == null || s.AuditoriumId == auditoriumId)
            .GroupBy(s => new { s.AuditoriumId, s.RowNumber })
            .Select(g => new
            {
                g.Key.AuditoriumId,
                g.Key.RowNumber,
                Total = g.Count(),
                Vip = g.Count(s => s.Type == SeatType.VIP)
            })
            .ToListAsync(ct);
        return rows.Where(r => r.Total == r.Vip).OrderBy(r => r.RowNumber).ToLookup(r => r.AuditoriumId, r => r.RowNumber);
    }

    private static AuditoriumDto ToDto(Auditorium a, int[] vipRows) =>
        new(a.Id, a.Name, a.Rows, a.SeatsPerRow, a.Rows * a.SeatsPerRow, vipRows);
}
