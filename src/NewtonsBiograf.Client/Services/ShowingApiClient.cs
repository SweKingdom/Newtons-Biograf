using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Client.Services;

/// <summary>Outcome of a showing API call. Only the list, get and settings calls fill their own property.</summary>
public sealed record ShowingResult(bool Success, ApiProblem? Problem = null, bool NotFound = false,
    ShowingDto[]? Showings = null, ShowingDto? Showing = null, ShowingSettingsDto? Settings = null);

/// <summary>Typed wrapper over the showing API so pages never parse HTTP themselves.</summary>
public sealed class ShowingApiClient(IHttpClientFactory factory)
{
    public const string ForbiddenMessage = "You do not have permission to manage showings.";
    public const string NotFoundMessage = "That showing no longer exists. The list has been refreshed.";

    private static JsonSerializerOptions Json => MovieApiClient.Json;

    /// <summary>Showings starting in [from, to), optionally in one auditorium. Times are sent as ISO UTC with "Z".</summary>
    public Task<ShowingResult> ListAsync(DateTime from, DateTime to, int? auditoriumId = null)
    {
        var url = $"api/showings?from={Iso(from)}&to={Iso(to)}"
            + (auditoriumId is { } id ? $"&auditoriumId={id}" : "");
        return SendAsync(HttpMethod.Get, url, null,
            async content => new ShowingResult(true, Showings: await content.ReadFromJsonAsync<ShowingDto[]>(Json) ?? []));
    }

    public Task<ShowingResult> GetAsync(int id) => SendAsync(HttpMethod.Get, $"api/showings/{id}", null,
        async content => new ShowingResult(true,
            Showing: await content.ReadFromJsonAsync<ShowingDto>(Json) ?? throw new JsonException("Empty body")));

    public Task<ShowingResult> GetSettingsAsync() => SendAsync(HttpMethod.Get, "api/showings/settings", null,
        async content => new ShowingResult(true,
            Settings: await content.ReadFromJsonAsync<ShowingSettingsDto>(Json) ?? throw new JsonException("Empty body")));

    public Task<ShowingResult> CreateAsync(ShowingRequest request) => SendAsync(HttpMethod.Post, "api/showings", AsUtc(request));

    public Task<ShowingResult> UpdateAsync(int id, ShowingRequest request) =>
        SendAsync(HttpMethod.Put, $"api/showings/{id}", AsUtc(request));

    public Task<ShowingResult> CancelAsync(int id) => SendAsync(HttpMethod.Post, $"api/showings/{id}/cancel", null);

    public Task<ShowingResult> DeleteAsync(int id) => SendAsync(HttpMethod.Delete, $"api/showings/{id}", null);

    private static string Iso(DateTime value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    /// <summary>The API requires an offset, so the start always travels as UTC (serialised with "Z").</summary>
    private static ShowingRequest AsUtc(ShowingRequest request) =>
        request with { StartTime = request.StartTime.ToUniversalTime() };

    private async Task<ShowingResult> SendAsync(HttpMethod method, string url, object? body,
        Func<HttpContent, Task<ShowingResult>>? read = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            }

            using var response = await factory.CreateClient("Api").SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return read is null ? new ShowingResult(true) : await read(response.Content);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Forbidden => new ShowingResult(false, new ApiProblem(ForbiddenMessage, null)),
                HttpStatusCode.NotFound => new ShowingResult(false, new ApiProblem(NotFoundMessage, null), NotFound: true),
                _ => new ShowingResult(false, await ApiProblem.ReadAsync(response))
            };
        }
        catch (HttpRequestException)
        {
            return new ShowingResult(false, ApiProblem.Unreachable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new ShowingResult(false, ApiProblem.Unexpected);
        }
    }
}

/// <summary>Conversions between the browser's local time (entry and display) and UTC (API and database).</summary>
public static class ShowingTimes
{
    /// <summary>A date and time typed in the browser's local zone, as UTC.</summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time) =>
        DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Local).ToUniversalTime();

    /// <summary>The end of a showing: start + movie length + cleaning minutes (cosmetic; the API computes the real one).</summary>
    public static DateTime End(DateTime start, int lengthMinutes, int cleaningMinutes) =>
        start.AddMinutes(lengthMinutes + cleaningMinutes);
}
