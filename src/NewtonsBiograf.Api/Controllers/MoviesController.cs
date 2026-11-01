using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

[ApiController]
[Route("api/movies")]
public class MoviesController(IRepository<Movie> movies, IRepository<Showing> showings) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<MovieDto[]>> List(CancellationToken ct)
    {
        var all = await movies.Query().AsNoTracking().OrderBy(m => m.Title).ToListAsync(ct);
        return all.Select(ToDto).ToArray();
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<MovieDto>> Get(int id, CancellationToken ct)
    {
        var movie = await movies.GetByIdAsync(id, ct);
        return movie is null ? NotFound() : ToDto(movie);
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManageMovies)]
    public async Task<ActionResult<MovieDto>> Create(MovieRequest request, CancellationToken ct)
    {
        var movie = new Movie();
        Apply(movie, request);
        await movies.AddAsync(movie, ct);
        await movies.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = movie.Id }, ToDto(movie));
    }

    /// <summary>The route id wins; any id in the body is ignored.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.ManageMovies)]
    public async Task<IActionResult> Update(int id, MovieRequest request, CancellationToken ct)
    {
        var movie = await movies.GetByIdAsync(id, ct);
        if (movie is null)
        {
            return NotFound();
        }

        Apply(movie, request);
        await movies.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Blocked by future non-cancelled showings. Showings.MovieId is Restrict (no cascade, to keep booking history),
    /// so a movie that has any other showing cannot be deleted either.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.ManageMovies)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var movie = await movies.GetByIdAsync(id, ct);
        if (movie is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        if (await showings.Query().AnyAsync(
                s => s.MovieId == id && s.StartTime > now && s.Status != ShowingStatus.Cancelled, ct))
        {
            return Problem(detail: "Movie cannot be deleted because it has future showings.",
                statusCode: StatusCodes.Status409Conflict);
        }
        if (await showings.Query().AnyAsync(s => s.MovieId == id, ct))
        {
            return Problem(detail: "Movie cannot be deleted because it has showings.",
                statusCode: StatusCodes.Status409Conflict);
        }

        movies.Remove(movie);
        await movies.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(Movie movie, MovieRequest request)
    {
        movie.Title = request.Title.Trim();
        movie.Genre = request.Genre!.Value;
        movie.AgeRating = request.AgeRating!.Value;
        movie.LengthMinutes = request.LengthMinutes;
        movie.Description = request.Description;
    }

    private static MovieDto ToDto(Movie m) =>
        new(m.Id, m.Title, m.Genre, m.AgeRating, m.LengthMinutes, m.Description);
}
