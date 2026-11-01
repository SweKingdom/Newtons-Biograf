using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Client.Services;

/// <summary>Outcome of a movie API call. <see cref="Movies"/> is set only by a successful list call.</summary>
public sealed record MovieResult(bool Success, ApiProblem? Problem = null, bool NotFound = false, MovieDto[]? Movies = null);

/// <summary>Typed wrapper over the movie API so pages never parse HTTP themselves.</summary>
public sealed class MovieApiClient(IHttpClientFactory factory)
{
    public const string ForbiddenMessage = "You do not have permission to manage movies.";
    public const string NotFoundMessage = "That movie no longer exists. The list has been refreshed.";

    /// <summary>Web defaults plus the API's enum rule: enums travel as their member names. Shared with the other API clients.</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StrictEnumConverterFactory() }
    };

    public Task<MovieResult> GetAllAsync() => SendAsync(HttpMethod.Get, "api/movies", null);

    public Task<MovieResult> CreateAsync(MovieRequest request) => SendAsync(HttpMethod.Post, "api/movies", request);

    public Task<MovieResult> UpdateAsync(int id, MovieRequest request) => SendAsync(HttpMethod.Put, $"api/movies/{id}", request);

    public Task<MovieResult> DeleteAsync(int id) => SendAsync(HttpMethod.Delete, $"api/movies/{id}", null);

    private async Task<MovieResult> SendAsync(HttpMethod method, string url, MovieRequest? body)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            using var response = await factory.CreateClient("Api").SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return method == HttpMethod.Get
                    ? new MovieResult(true, Movies: await response.Content.ReadFromJsonAsync<MovieDto[]>(Json) ?? [])
                    : new MovieResult(true);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Forbidden => new MovieResult(false, new ApiProblem(ForbiddenMessage, null)),
                HttpStatusCode.NotFound => new MovieResult(false, new ApiProblem(NotFoundMessage, null), NotFound: true),
                _ => new MovieResult(false, await ApiProblem.ReadAsync(response))
            };
        }
        catch (HttpRequestException)
        {
            return new MovieResult(false, ApiProblem.Unreachable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new MovieResult(false, ApiProblem.Unexpected);
        }
    }
}
