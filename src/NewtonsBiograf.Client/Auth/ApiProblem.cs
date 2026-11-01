using System.Net.Http.Json;
using System.Text.Json;

namespace NewtonsBiograf.Client.Auth;

/// <summary>An API error (ProblemDetails / ValidationProblemDetails) turned into messages a form can show.</summary>
public sealed class ApiProblem
{
    private const string Fallback = "Something went wrong. Please try again.";

    private readonly string? _title;
    private readonly Dictionary<string, string[]> _errors;

    public ApiProblem(string? title, Dictionary<string, string[]>? errors)
    {
        _title = title;
        _errors = new Dictionary<string, string[]>(errors ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public static ApiProblem Unreachable { get; } =
        new("Could not reach the server. Check your connection and try again.", null);

    public static ApiProblem Unexpected { get; } = new(null, null);

    internal sealed record Body(string? Title, Dictionary<string, string[]>? Errors);

    public static async Task<ApiProblem> ReadAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<Body>();
            return new ApiProblem(body?.Title, body?.Errors);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return Unexpected;
        }
    }

    /// <summary>The error for one form field (case-insensitive match), or null.</summary>
    public string? Field(string name) => _errors.TryGetValue(name, out var messages) ? string.Join(" ", messages) : null;

    /// <summary>Messages not tied to one of the given fields, or the problem title when there are no field errors.</summary>
    public string? General(params string[] knownFields)
    {
        var other = _errors.Where(e => !knownFields.Contains(e.Key, StringComparer.OrdinalIgnoreCase))
            .SelectMany(e => e.Value).ToArray();
        if (other.Length > 0)
        {
            return string.Join(" ", other);
        }
        return _errors.Count == 0 ? _title ?? Fallback : null;
    }
}
