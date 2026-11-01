using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Client.Services;

/// <summary>The public filters of the home page. Empty values are left out of the request.</summary>
public sealed record ShowingFilterModel
{
    public string? Genre { get; init; }
    public DateOnly? Date { get; init; }
    public int? AuditoriumId { get; init; }
    public string? Title { get; init; }
}

/// <summary>Outcome of an API read: the value, or a plain-language error, or NotFound (404).</summary>
public sealed record PublicResult<T>(T? Value, string? Error = null, bool NotFound = false)
{
    public bool Success => Error is null && !NotFound;
}

/// <summary>Typed client for the anonymous public endpoints; needs no sign-in.</summary>
public sealed class PublicApiClient(HttpClient http)
{
    internal const string Fallback = "Something went wrong. Please try again.";

    public Task<PublicResult<ShowingListItem[]>> GetShowingsAsync(ShowingFilterModel? filter = null) =>
        GetAsync<ShowingListItem[]>(http, "api/public/showings" + Query(filter));

    public Task<PublicResult<string[]>> GetGenresAsync() => GetAsync<string[]>(http, "api/public/genres");

    public Task<PublicResult<AuditoriumOption[]>> GetAuditoriumsAsync() => GetAsync<AuditoriumOption[]>(http, "api/public/auditoriums");

    public Task<PublicResult<MovieDetailDto>> GetMovieAsync(int id) => GetAsync<MovieDetailDto>(http, $"api/public/movies/{id}");

    /// <summary>The query string for the filter (with the leading "?"), or "" when nothing is set.</summary>
    internal static string Query(ShowingFilterModel? filter)
    {
        var parts = new List<string>();
        Add(parts, "genre", filter?.Genre);
        Add(parts, "date", filter?.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parts, "auditoriumId", filter?.AuditoriumId?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "title", filter?.Title);
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }

    /// <summary>GETs <paramref name="url"/> and reads the JSON body; failures become a plain-language error, never an exception.</summary>
    internal static async Task<PublicResult<T>> GetAsync<T>(HttpClient http, string url)
    {
        try
        {
            using var response = await http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                return new PublicResult<T>(
                    await response.Content.ReadFromJsonAsync<T>(MovieApiClient.Json) ?? throw new JsonException("Empty body"));
            }

            return response.StatusCode == HttpStatusCode.NotFound
                ? new PublicResult<T>(default, NotFound: true)
                : new PublicResult<T>(default, (await ApiProblem.ReadAsync(response)).General() ?? Fallback);
        }
        catch (HttpRequestException)
        {
            return new PublicResult<T>(default, ApiProblem.Unreachable.General() ?? Fallback);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new PublicResult<T>(default, Fallback);
        }
    }
}

/// <summary>Showing times are displayed in the cinema's time zone (<c>Showings:DisplayTimeZone</c>), not the browser's.</summary>
public static class DisplayTime
{
    /// <summary>The zone with this IANA id, or the browser's own zone when the id is missing or unknown.</summary>
    public static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    public static DateTime ToZone(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    public static DateOnly Today(TimeZoneInfo zone) => DateOnly.FromDateTime(ToZone(DateTime.UtcNow, zone));

    /// <summary>The showings grouped by their calendar day in <paramref name="zone"/>, in the order given.</summary>
    public static IEnumerable<IGrouping<DateTime, ShowingListItem>> GroupByDay(IEnumerable<ShowingListItem> showings, TimeZoneInfo zone) =>
        showings.GroupBy(s => ToZone(s.Start, zone).Date);
}
