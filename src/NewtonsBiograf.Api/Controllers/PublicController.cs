using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

/// <summary>Anonymous, read-only endpoints for visitors. Never returns user data.</summary>
[ApiController]
[Route("api/public")]
[AllowAnonymous]
public class PublicController(AppDbContext db, IOptions<ShowingOptions> options, TimeProvider clock) : ControllerBase
{
    private const int WindowDays = 14;
    private const int MaxTitleLength = 100;
    private const int MaxResults = 500;

    /// <summary>
    /// Non-cancelled showings starting after now and up to 14 days ahead, by start time. The optional filters are
    /// combined with AND: genre (display or enum name, any case), date (yyyy-MM-dd in the display time zone),
    /// auditoriumId, and title (partial, case-insensitive).
    /// </summary>
    [HttpGet("showings")]
    public async Task<ActionResult<ShowingListItem[]>> Showings(
        string? genre, string? date, int? auditoriumId, string? title, CancellationToken ct)
    {
        title = title?.Trim();
        if (title?.Length > MaxTitleLength)
        {
            ModelState.AddModelError(nameof(title), $"The query parameter 'title' must be at most {MaxTitleLength} characters.");
        }

        DateTime? dayStart = null;
        DateTime? dayEnd = null;
        if (!string.IsNullOrWhiteSpace(date))
        {
            if (DateTime.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.DisplayTimeZone);
                dayStart = TimeZoneInfo.ConvertTimeToUtc(day, zone);
                dayEnd = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1), zone);
            }
            else
            {
                ModelState.AddModelError(nameof(date), "The query parameter 'date' must be a date written yyyy-MM-dd.");
            }
        }
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var last = now.AddDays(WindowDays);
        var query = db.Showings.AsNoTracking()
            .Where(s => s.Status != ShowingStatus.Cancelled && s.StartTime > now && s.StartTime <= last);
        if (!string.IsNullOrWhiteSpace(genre))
        {
            var name = genre.Trim();
            var matches = Enum.GetValues<Genre>().Where(g =>
                string.Equals(g.ToDisplayName(), name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(g.ToString(), name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                return Array.Empty<ShowingListItem>();
            }

            var wanted = matches[0];
            query = query.Where(s => s.Movie!.Genre == wanted);
        }
        if (dayStart is not null)
        {
            query = query.Where(s => s.StartTime >= dayStart && s.StartTime < dayEnd);
        }
        if (auditoriumId is not null)
        {
            query = query.Where(s => s.AuditoriumId == auditoriumId);
        }

        var rows = await query.OrderBy(s => s.StartTime).ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.MovieId,
                s.Movie!.Title,
                s.Movie.Genre,
                s.Movie.AgeRating,
                s.Movie.LengthMinutes,
                s.AuditoriumId,
                AuditoriumName = s.Auditorium!.Name,
                s.StartTime,
                s.EndTime,
                FreeSeats = s.Auditorium.Seats.Count()
                    - db.BookingSeats.Count(bs => bs.ShowingId == s.Id && bs.Booking!.Status == BookingStatus.Confirmed)
            })
            .ToListAsync(ct);

        // SQLite LIKE only ignores case for ASCII (not å/ä/ö), so the title is matched in memory over the small 14-day set.
        return rows
            .Where(r => string.IsNullOrEmpty(title) || r.Title.Contains(title, StringComparison.OrdinalIgnoreCase))
            .Take(MaxResults)
            .Select(r => new ShowingListItem(r.Id, r.MovieId, r.Title, r.Genre.ToDisplayName(), r.AgeRating.ToDisplayName(),
                r.LengthMinutes, r.AuditoriumId, r.AuditoriumName,
                DateTime.SpecifyKind(r.StartTime, DateTimeKind.Utc), DateTime.SpecifyKind(r.EndTime, DateTimeKind.Utc),
                r.FreeSeats))
            .ToArray();
    }

    /// <summary>Distinct genres of the movies (display names), sorted; feeds the filter dropdown.</summary>
    [HttpGet("genres")]
    public async Task<ActionResult<string[]>> Genres(CancellationToken ct)
    {
        var genres = await db.Movies.AsNoTracking().Select(m => m.Genre).Distinct().ToListAsync(ct);
        return genres.Select(g => g.ToDisplayName()).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Id and name of every auditorium, sorted by name; feeds the filter dropdown.</summary>
    [HttpGet("auditoriums")]
    public async Task<ActionResult<AuditoriumOption[]>> Auditoriums(CancellationToken ct) =>
        await db.Auditoriums.AsNoTracking().OrderBy(a => a.Name).Select(a => new AuditoriumOption(a.Id, a.Name))
            .ToArrayAsync(ct);
}
