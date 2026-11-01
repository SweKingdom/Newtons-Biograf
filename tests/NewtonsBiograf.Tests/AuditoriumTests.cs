using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewtonsBiograf.Api.Auth;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

public sealed class AuditoriumTests : IAsyncLifetime
{
    private const string Url = "/api/auditoriums";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _connection.Open();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
            }));

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private string Token(string role, params string[] permissions) =>
        _factory.Services.GetRequiredService<TokenService>().Create(
            new ApplicationUser { Id = "caller-1", Email = "caller@example.com", DisplayName = "Caller" }, role, permissions).Token;

    private string AdminToken => Token(Roles.Admin);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        using var client = _factory.CreateClient();
        return await client.SendAsync(request);
    }

    private static object Body(string? name = "Salong 1", object? rows = null, object? perRow = null) =>
        new { Name = name, Rows = rows ?? 5, SeatsPerRow = perRow ?? 8 };

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        return new Dictionary<string, string[]>(problem!.Errors, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> AuditoriumCount() => WithDb(db => db.Auditoriums.CountAsync());

    private Task<int> SeatCount() => WithDb(db => db.Seats.CountAsync());

    private Task<List<Seat>> SeatsOf(int auditoriumId) =>
        WithDb(db => db.Seats.Where(s => s.AuditoriumId == auditoriumId).OrderBy(s => s.RowNumber).ThenBy(s => s.SeatNumber).ToListAsync());

    private async Task<int> Create(string name = "Salong 1", int rows = 3, int perRow = 4)
    {
        var response = await Send(HttpMethod.Post, Url, AdminToken, Body(name, rows, perRow));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuditoriumDto>(Json))!.Id;
    }

    private Task SetVip(int auditoriumId, params int[] rows) => WithDb(async db =>
    {
        foreach (var seat in db.Seats.Where(s => s.AuditoriumId == auditoriumId && rows.Contains(s.RowNumber)))
        {
            seat.Type = SeatType.VIP;
        }
        return await db.SaveChangesAsync();
    });

    /// <summary>Books one seat of the auditorium in a new showing that starts after <paramref name="startsIn"/>.</summary>
    private Task SeedBooking(int auditoriumId, TimeSpan startsIn, ShowingStatus status, int row, int number, bool active = true) =>
        WithDb(async db =>
        {
            var seat = await db.Seats.SingleAsync(s => s.AuditoriumId == auditoriumId && s.RowNumber == row && s.SeatNumber == number);
            var key = Guid.NewGuid().ToString("N");
            var start = DateTime.UtcNow + startsIn;
            var showing = new Showing
            {
                Movie = new Movie { Title = "Dune", Genre = Genre.Drama, AgeRating = AgeRating.Age15, LengthMinutes = 100 },
                AuditoriumId = auditoriumId,
                StartTime = start,
                EndTime = start.AddHours(2),
                Status = status
            };
            var user = new ApplicationUser { UserName = key, Email = $"{key}@example.com", DisplayName = "Booker" };
            db.AddRange(showing, user);
            await db.SaveChangesAsync();
            db.Bookings.Add(new Booking
            {
                Reference = key[..8],
                UserId = user.Id,
                ShowingId = showing.Id,
                TotalPrice = 100,
                Status = BookingStatus.Confirmed,
                CreatedAt = DateTime.UtcNow,
                Seats = [new BookingSeat { ShowingId = showing.Id, SeatId = seat.Id, Price = 100, IsActive = active }]
            });
            return await db.SaveChangesAsync();
        });

    [Fact]
    public async Task Ac1_Create_returns_201_with_location_capacity_and_exactly_the_generated_standard_seats()
    {
        var response = await Send(HttpMethod.Post, Url, AdminToken, Body("  Salong 1  ", 3, 4));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<AuditoriumDto>(Json))!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal("Salong 1", created.Name);
        Assert.Equal((3, 4, 12), (created.Rows, created.SeatsPerRow, created.Capacity));
        Assert.Empty(created.VipRows);

        var seats = await SeatsOf(created.Id);
        Assert.Equal(12, seats.Count);
        Assert.Equal(
            Enumerable.Range(1, 3).SelectMany(r => Enumerable.Range(1, 4).Select(n => (r, n))),
            seats.Select(s => (s.RowNumber, s.SeatNumber)));
        Assert.All(seats, s => Assert.Equal(SeatType.Standard, s.Type));
    }

    [Fact]
    public async Task Ac1_Create_accepts_the_boundary_sizes()
    {
        await Create("Smallest", 1, 1);
        var largest = await Create("Largest", 26, 40);

        Assert.Equal(1 + 1040, await SeatCount());
        Assert.Equal(1040, (await SeatsOf(largest)).Count);
    }

    [Fact]
    public async Task Ac2_Create_with_a_name_equal_ignoring_case_and_spaces_returns_400_and_creates_nothing()
    {
        await Create("Salong 1", 2, 2);

        var response = await Send(HttpMethod.Post, Url, AdminToken, Body("  SALONG 1 ", 5, 5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))["Name"]);
        Assert.Equal(1, await AuditoriumCount());
        Assert.Equal(4, await SeatCount());
    }

    [Fact]
    public async Task Ac2_Put_rejects_another_auditoriums_name_but_allows_its_own_in_a_different_case()
    {
        var first = await Create("Salong 1");
        var second = await Create("Salong 2");

        var clash = await Send(HttpMethod.Put, $"{Url}/{second}", AdminToken, Body("salong 1", 3, 4));
        var own = await Send(HttpMethod.Put, $"{Url}/{first}", AdminToken, Body("SALONG 1", 3, 4));

        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        Assert.NotEmpty((await Errors(clash))["Name"]);
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        var names = await WithDb(db => db.Auditoriums.OrderBy(a => a.Id).Select(a => a.Name).ToListAsync());
        Assert.Equal(["SALONG 1", "Salong 2"], names);
    }

    [Theory]
    [InlineData("", 5, 8, "Name")]
    [InlineData("   ", 5, 8, "Name")]
    [InlineData("A", 0, 8, "Rows")]
    [InlineData("A", 27, 8, "Rows")]
    [InlineData("A", -1, 8, "Rows")]
    [InlineData("A", 5, 0, "SeatsPerRow")]
    [InlineData("A", 5, 41, "SeatsPerRow")]
    public async Task Ac3_Invalid_fields_return_400_keyed_by_field_and_create_nothing(string name, int rows, int perRow, string field)
    {
        var response = await Send(HttpMethod.Post, Url, AdminToken, Body(name, rows, perRow));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))[field]);
        Assert.Equal(0, await AuditoriumCount());
        Assert.Equal(0, await SeatCount());
    }

    [Fact]
    public async Task Ac3_Name_over_100_characters_is_rejected_and_100_is_accepted_and_missing_fields_are_all_reported()
    {
        var tooLong = await Send(HttpMethod.Post, Url, AdminToken, Body(new string('n', 101)));
        var max = await Send(HttpMethod.Post, Url, AdminToken, Body(new string('n', 100)));
        var empty = await Send(HttpMethod.Post, Url, AdminToken, new { });

        Assert.Equal("Name must be at most 100 characters", Assert.Single((await Errors(tooLong))["Name"]));
        Assert.Equal(HttpStatusCode.Created, max.StatusCode);
        var errors = await Errors(empty);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("Name is required", Assert.Single(errors["Name"]));
        Assert.NotEmpty(errors["Rows"]);
        Assert.NotEmpty(errors["SeatsPerRow"]);
    }

    [Theory]
    [InlineData("Rows", 1.5)]
    [InlineData("Rows", "abc")]
    [InlineData("SeatsPerRow", 2.5)]
    public async Task Ac3_Non_integer_numbers_fail_binding_with_400_keyed_by_field(string field, object value)
    {
        var body = new Dictionary<string, object> { ["Name"] = "A", ["Rows"] = 5, ["SeatsPerRow"] = 8 };
        body[field] = value;

        var response = await Send(HttpMethod.Post, Url, AdminToken, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await Errors(response))[field]);
        Assert.Equal(0, await AuditoriumCount());
    }

    [Fact]
    public async Task Ac3_Put_invalid_returns_400_and_changes_nothing()
    {
        var id = await Create();

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", AdminToken, Body("", 27, 41));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await Errors(response);
        Assert.NotEmpty(errors["Name"]);
        Assert.NotEmpty(errors["Rows"]);
        Assert.NotEmpty(errors["SeatsPerRow"]);
        Assert.Equal(12, await SeatCount());
    }

    [Fact]
    public async Task Ac4_Anonymous_list_is_ordered_by_name_and_shows_capacity_and_sorted_vip_rows_per_auditorium()
    {
        var b = await Create("B room", 4, 2);
        var a = await Create("A room", 2, 3);
        await SetVip(b, 4, 1);
        await WithDb(async db =>
        {
            // Row 2 of B is only partly VIP, so it is not a VIP row.
            (await db.Seats.FirstAsync(s => s.AuditoriumId == b && s.RowNumber == 2 && s.SeatNumber == 1)).Type = SeatType.VIP;
            return await db.SaveChangesAsync();
        });

        var response = await Send(HttpMethod.Get, Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<AuditoriumDto[]>(Json))!;
        Assert.Equal(["A room", "B room"], list.Select(x => x.Name));
        Assert.Equal((a, 2, 3, 6), (list[0].Id, list[0].Rows, list[0].SeatsPerRow, list[0].Capacity));
        Assert.Empty(list[0].VipRows);
        Assert.Equal((b, 4, 2, 8), (list[1].Id, list[1].Rows, list[1].SeatsPerRow, list[1].Capacity));
        Assert.Equal([1, 4], list[1].VipRows);
    }

    [Fact]
    public async Task Ac4_Anonymous_get_by_id_returns_200_and_unknown_id_returns_404()
    {
        var id = await Create("Salong 1", 2, 5);
        await SetVip(id, 2);

        var found = await Send(HttpMethod.Get, $"{Url}/{id}");
        var missing = await Send(HttpMethod.Get, $"{Url}/9999");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var dto = (await found.Content.ReadFromJsonAsync<AuditoriumDto>(Json))!;
        Assert.Equal((id, "Salong 1", 2, 5, 10), (dto.Id, dto.Name, dto.Rows, dto.SeatsPerRow, dto.Capacity));
        Assert.Equal([2], dto.VipRows);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Ac5_Put_name_only_returns_204_and_leaves_every_seat_untouched()
    {
        var id = await Create("Salong 1");
        var before = await SeatsOf(id);

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", AdminToken, Body(" Renamed ", 3, 4));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Renamed", (await WithDb(db => db.Auditoriums.SingleAsync())).Name);
        Assert.Equal(before.Select(s => (s.Id, s.RowNumber, s.SeatNumber)),
            (await SeatsOf(id)).Select(s => (s.Id, s.RowNumber, s.SeatNumber)));
    }

    [Fact]
    public async Task Ac5_Growing_the_layout_keeps_existing_seats_and_new_seats_take_their_rows_type()
    {
        var id = await Create("Salong 1", 3, 4);
        await SetVip(id, 2);
        var before = (await SeatsOf(id)).ToDictionary(s => (s.RowNumber, s.SeatNumber), s => s.Id);

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", AdminToken, Body("Salong 1", 4, 6));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await SeatsOf(id);
        Assert.Equal(24, after.Count);
        Assert.All(before, kv => Assert.Equal(kv.Value, after.Single(s => (s.RowNumber, s.SeatNumber) == kv.Key).Id));
        Assert.All(after, s => Assert.Equal(
            s.RowNumber == 2 ? SeatType.VIP : SeatType.Standard, s.Type));
        var dto = (await (await Send(HttpMethod.Get, $"{Url}/{id}")).Content.ReadFromJsonAsync<AuditoriumDto>(Json))!;
        Assert.Equal((4, 6, 24), (dto.Rows, dto.SeatsPerRow, dto.Capacity));
        Assert.Equal([2], dto.VipRows);
    }

    [Fact]
    public async Task Ac5_Shrinking_the_layout_deletes_extra_seats_and_keeps_the_rest_with_the_same_ids()
    {
        var id = await Create("Salong 1", 3, 4);
        var before = (await SeatsOf(id)).ToDictionary(s => (s.RowNumber, s.SeatNumber), s => s.Id);

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", AdminToken, Body("Salong 1", 2, 2));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await SeatsOf(id);
        Assert.Equal([(1, 1), (1, 2), (2, 1), (2, 2)], after.Select(s => (s.RowNumber, s.SeatNumber)));
        Assert.All(after, s => Assert.Equal(before[(s.RowNumber, s.SeatNumber)], s.Id));
    }

    [Fact]
    public async Task Ac5_Put_unknown_id_returns_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, $"{Url}/9999", AdminToken, Body())).StatusCode);
    }

    [Fact]
    public async Task Ac6_Layout_change_with_an_active_booking_in_a_future_showing_returns_409_and_changes_nothing()
    {
        var id = await Create("Salong 1", 3, 4);
        await SeedBooking(id, TimeSpan.FromDays(1), ShowingStatus.Scheduled, 1, 1);
        var before = (await SeatsOf(id)).Select(s => s.Id);

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", AdminToken, Body("Renamed", 6, 6));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Layout cannot be changed because the auditorium has bookings.",
            (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
        var auditorium = await WithDb(db => db.Auditoriums.SingleAsync());
        Assert.Equal(("Salong 1", 3, 4), (auditorium.Name, auditorium.Rows, auditorium.SeatsPerRow));
        Assert.Equal(before, (await SeatsOf(id)).Select(s => s.Id));
    }

    [Fact]
    public async Task Ac6_Removing_a_seat_that_was_ever_booked_returns_409_even_for_a_past_showing()
    {
        var id = await Create("Salong 1", 3, 4);
        await SeedBooking(id, TimeSpan.FromDays(-1), ShowingStatus.Scheduled, 3, 4, active: false);

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", AdminToken, Body("Salong 1", 3, 3));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(12, await SeatCount());
    }

    [Theory]
    [InlineData(-1, ShowingStatus.Scheduled, true)]
    [InlineData(1, ShowingStatus.Cancelled, true)]
    [InlineData(1, ShowingStatus.Scheduled, false)]
    public async Task Ac6_Layout_change_is_allowed_when_bookings_are_past_cancelled_or_inactive_and_seats_that_stay(
        int days, ShowingStatus status, bool active)
    {
        var id = await Create("Salong 1", 3, 4);
        await SeedBooking(id, TimeSpan.FromDays(days), status, 1, 1, active);

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", AdminToken, Body("Salong 1", 2, 2));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(4, await SeatCount());
    }

    [Fact]
    public async Task Ac6_Name_only_change_is_allowed_even_with_future_bookings()
    {
        var id = await Create("Salong 1", 3, 4);
        await SeedBooking(id, TimeSpan.FromDays(1), ShowingStatus.Scheduled, 1, 1);

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", AdminToken, Body("Renamed", 3, 4));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Renamed", (await WithDb(db => db.Auditoriums.SingleAsync())).Name);
    }

    [Fact]
    public async Task Ac6_A_booking_in_another_auditorium_does_not_block_the_change()
    {
        var other = await Create("Other", 3, 4);
        var id = await Create("Salong 1", 3, 4);
        await SeedBooking(other, TimeSpan.FromDays(1), ShowingStatus.Scheduled, 1, 1);

        var response = await Send(HttpMethod.Put, $"{Url}/{id}", AdminToken, Body("Salong 1", 2, 2));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(null, null, 401)]
    [InlineData(Roles.Customer, null, 403)]
    [InlineData(Roles.Staff, Permissions.ManageShowings, 403)]
    [InlineData(Roles.Admin, null, 0)]
    public async Task Ac7_Write_endpoints_by_caller(string? role, string? permission, int expectedDenied)
    {
        var id = await Create("Existing");
        var token = role is null ? null : Token(role, permission is null ? Array.Empty<string>() : new[] { permission });

        var post = await Send(HttpMethod.Post, Url, token, Body("New"));
        var put = await Send(HttpMethod.Put, $"{Url}/{id}", token, Body("Changed", 3, 4));

        if (expectedDenied == 0)
        {
            Assert.Equal(HttpStatusCode.Created, post.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }
        else
        {
            Assert.All(new[] { post, put }, r => Assert.Equal(expectedDenied, (int)r.StatusCode));
            Assert.Equal(["Existing"], await WithDb(db => db.Auditoriums.Select(a => a.Name).ToListAsync()));
        }
    }
}
