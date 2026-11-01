using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NewtonsBiograf.Client.Auth;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Client.Services;

/// <summary>Outcome of creating a booking: exactly one of the booking, a seat conflict (409 with the fresh map) or an error message.</summary>
public sealed record BookingCreateResult(BookingDto? Booking = null, SeatConflictDto? Conflict = null, string? Error = null);

/// <summary>Typed client for the seat map and booking endpoints; goes through the "Api" client so the token is attached.</summary>
public sealed class BookingApiClient(IHttpClientFactory factory)
{
    private static JsonSerializerOptions Json => MovieApiClient.Json;

    public Task<PublicResult<SeatMapDto>> GetSeatMapAsync(int showingId) =>
        PublicApiClient.GetAsync<SeatMapDto>(factory.CreateClient("Api"), $"api/showings/{showingId}/seats");

    public Task<PublicResult<BookingDto[]>> GetMineAsync(BookingScope scope) =>
        PublicApiClient.GetAsync<BookingDto[]>(factory.CreateClient("Api"),
            $"api/bookings/mine?scope={scope.ToString().ToLowerInvariant()}");

    public async Task<BookingCreateResult> CreateAsync(CreateBookingRequest request)
    {
        try
        {
            using var response = await factory.CreateClient("Api").PostAsJsonAsync("api/bookings", request, Json);
            if (response.IsSuccessStatusCode)
            {
                return new BookingCreateResult(
                    await response.Content.ReadFromJsonAsync<BookingDto>(Json) ?? throw new JsonException("Empty body"));
            }

            if (response.StatusCode == HttpStatusCode.Conflict && await ReadConflictAsync(response) is { } conflict)
            {
                return new BookingCreateResult(Conflict: conflict);
            }

            return new BookingCreateResult(Error: (await ApiProblem.ReadAsync(response)).General() ?? PublicApiClient.Fallback);
        }
        catch (HttpRequestException)
        {
            return new BookingCreateResult(Error: ApiProblem.Unreachable.General());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new BookingCreateResult(Error: PublicApiClient.Fallback);
        }
    }

    /// <summary>
    /// The seat-conflict body of a 409, or null when the 409 is another problem (for example a closed showing).
    /// Reads the body as text so the problem reader can still read the response afterwards.
    /// </summary>
    private static async Task<SeatConflictDto?> ReadConflictAsync(HttpResponseMessage response)
    {
        try
        {
            var conflict = JsonSerializer.Deserialize<SeatConflictDto>(await response.Content.ReadAsStringAsync(), Json);
            return conflict?.SeatMap is null ? null : conflict;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The selection rules of the seat map, kept apart from the page so they can be tested.</summary>
public static class SeatSelection
{
    /// <summary>Selects a free seat or deselects a selected one. Returns a message when the toggle is refused, otherwise null.</summary>
    public static string? Toggle(HashSet<int> selected, SeatDto seat)
    {
        if (seat.IsTaken)
        {
            return null;
        }
        if (selected.Remove(seat.SeatId))
        {
            return null;
        }
        if (selected.Count >= BookingLimits.MaxSeats)
        {
            return $"You can select at most {BookingLimits.MaxSeats} seats.";
        }
        selected.Add(seat.SeatId);
        return null;
    }

    /// <summary>After a conflict: drops the seats that are taken in the refreshed map; still-free selections stay.</summary>
    public static void KeepFree(HashSet<int> selected, SeatMapDto map) =>
        selected.IntersectWith(map.Seats.Where(s => !s.IsTaken).Select(s => s.SeatId));

    /// <summary>Display total in SEK: the sum of the selected seats' prices (the server calculates the real one).</summary>
    public static int Total(SeatMapDto map, IReadOnlySet<int> selected) =>
        map.Seats.Where(s => selected.Contains(s.SeatId)).Sum(s => s.Price);
}

/// <summary>Text shown for bookings on the confirmation and my-bookings screens.</summary>
public static class BookingText
{
    public static string Seats(BookingDto booking) =>
        string.Join(", ", booking.Seats.Select(s => $"Row {s.Row} seat {s.Number} ({s.Type})"));

    public static string Sek(decimal amount) => $"{amount.ToString("0.##", CultureInfo.InvariantCulture)} SEK";
}
