using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Client.Services;

/// <summary>Outcome of an auditorium or pricing call. Only the list, get and prices calls fill their own property.</summary>
public sealed record AuditoriumResult(bool Success, ApiProblem? Problem = null, bool NotFound = false,
    AuditoriumDto[]? Auditoriums = null, AuditoriumDto? Auditorium = null, PricingDto? Prices = null);

/// <summary>Typed wrapper over the auditorium and pricing API so pages never parse HTTP themselves.</summary>
public sealed class AuditoriumApiClient(IHttpClientFactory factory)
{
    public const string ForbiddenMessage = "You do not have permission to manage auditoriums.";
    public const string NotFoundMessage = "That auditorium no longer exists.";

    private static JsonSerializerOptions Json => MovieApiClient.Json;

    public Task<AuditoriumResult> ListAsync() => SendAsync(HttpMethod.Get, "api/auditoriums", null,
        async content => new AuditoriumResult(true, Auditoriums: await content.ReadFromJsonAsync<AuditoriumDto[]>(Json) ?? []));

    public Task<AuditoriumResult> GetAsync(int id) => SendAsync(HttpMethod.Get, $"api/auditoriums/{id}", null,
        async content => new AuditoriumResult(true,
            Auditorium: await content.ReadFromJsonAsync<AuditoriumDto>(Json) ?? throw new JsonException("Empty body")));

    public Task<AuditoriumResult> GetPricesAsync() => SendAsync(HttpMethod.Get, "api/pricing", null,
        async content => new AuditoriumResult(true,
            Prices: await content.ReadFromJsonAsync<PricingDto>(Json) ?? throw new JsonException("Empty body")));

    public Task<AuditoriumResult> CreateAsync(AuditoriumRequest request) => SendAsync(HttpMethod.Post, "api/auditoriums", request);

    public Task<AuditoriumResult> UpdateAsync(int id, AuditoriumRequest request) =>
        SendAsync(HttpMethod.Put, $"api/auditoriums/{id}", request);

    public Task<AuditoriumResult> SetRowTypeAsync(int id, int row, SeatType type) =>
        SendAsync(HttpMethod.Put, $"api/auditoriums/{id}/rows/{row}/type", new RowTypeRequest(type));

    private async Task<AuditoriumResult> SendAsync(HttpMethod method, string url, object? body,
        Func<HttpContent, Task<AuditoriumResult>>? read = null)
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
                return read is null ? new AuditoriumResult(true) : await read(response.Content);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Forbidden => new AuditoriumResult(false, new ApiProblem(ForbiddenMessage, null)),
                HttpStatusCode.NotFound => new AuditoriumResult(false, new ApiProblem(NotFoundMessage, null), NotFound: true),
                _ => new AuditoriumResult(false, await ApiProblem.ReadAsync(response))
            };
        }
        catch (HttpRequestException)
        {
            return new AuditoriumResult(false, ApiProblem.Unreachable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new AuditoriumResult(false, ApiProblem.Unexpected);
        }
    }
}
