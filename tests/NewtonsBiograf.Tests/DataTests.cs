using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewtonsBiograf.Api.Data;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Tests;

public sealed class DataTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public DataTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private List<string> Scalars(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }
        return values;
    }

    /// <summary>Seeds one showing with two seats; returns their ids.</summary>
    private static (int ShowingId, int SeatId, int OtherSeatId, string UserId) SeedShowing(AppDbContext db)
    {
        var user = new ApplicationUser { UserName = "anna@example.com", DisplayName = "Anna" };
        var movie = new Movie { Title = "Dune", Genre = Genre.SciFi, AgeRating = AgeRating.Age11, LengthMinutes = 155 };
        var auditorium = new Auditorium
        {
            Name = "Salong 1",
            Rows = 1,
            SeatsPerRow = 2,
            Seats =
            [
                new Seat { RowNumber = 1, SeatNumber = 1, Type = SeatType.Standard },
                new Seat { RowNumber = 1, SeatNumber = 2, Type = SeatType.VIP }
            ]
        };
        var showing = new Showing
        {
            Movie = movie,
            Auditorium = auditorium,
            StartTime = new DateTime(2027, 1, 1, 18, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2027, 1, 1, 20, 35, 0, DateTimeKind.Utc)
        };
        db.Users.Add(user);
        db.Showings.Add(showing);
        db.SaveChanges();
        return (showing.Id, auditorium.Seats[0].Id, auditorium.Seats[1].Id, user.Id);
    }

    private static Booking NewBooking(int showingId, string userId, string reference) => new()
    {
        Reference = reference,
        UserId = userId,
        ShowingId = showingId,
        TotalPrice = 120m,
        Status = BookingStatus.Confirmed,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public void Ac1_Migration_creates_entity_and_identity_tables()
    {
        var tables = Scalars("SELECT name FROM sqlite_master WHERE type = 'table'");

        string[] expected =
        [
            "Movie", "Auditorium", "Seat", "Showing", "Booking", "BookingSeat",
            "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims",
            "AspNetRoleClaims", "AspNetUserLogins", "AspNetUserTokens"
        ];
        Assert.All(expected, name => Assert.Contains(name, tables));
    }

    [Fact]
    public void Ac1_Enums_are_stored_as_strings()
    {
        using var db = CreateContext();
        db.Movies.Add(new Movie { Title = "Dune", Genre = Genre.SciFi, AgeRating = AgeRating.Age11, LengthMinutes = 155 });
        db.SaveChanges();

        Assert.Equal("SciFi|Age11", Scalars("SELECT Genre || '|' || AgeRating FROM Movie").Single());
    }

    [Fact]
    public void Ac1_Movie_length_must_be_positive()
    {
        using var db = CreateContext();
        db.Movies.Add(new Movie { Title = "Empty", Genre = Genre.Drama, AgeRating = AgeRating.AllAges, LengthMinutes = 0 });

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Ac2_Auditorium_name_index_exists_and_duplicate_name_throws()
    {
        Assert.Contains("IX_Auditorium_Name", Scalars("SELECT name FROM sqlite_master WHERE type = 'index'"));

        using var db = CreateContext();
        db.Auditoriums.Add(new Auditorium { Name = "Salong 1", Rows = 5, SeatsPerRow = 8 });
        db.SaveChanges();
        db.Auditoriums.Add(new Auditorium { Name = "Salong 1", Rows = 4, SeatsPerRow = 6 });

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Ac2_Second_active_booking_seat_for_same_showing_and_seat_throws()
    {
        var indexSql = Scalars("SELECT sql FROM sqlite_master WHERE name = 'IX_BookingSeat_ShowingId_SeatId'").Single();
        Assert.Contains("UNIQUE", indexSql);
        Assert.Contains("\"IsActive\" = 1", indexSql);

        using var db = CreateContext();
        var (showingId, seatId, _, userId) = SeedShowing(db);
        var first = NewBooking(showingId, userId, "AAAA1111");
        first.Seats.Add(new BookingSeat { ShowingId = showingId, SeatId = seatId, Price = 120m });
        var second = NewBooking(showingId, userId, "BBBB2222");
        second.Seats.Add(new BookingSeat { ShowingId = showingId, SeatId = seatId, Price = 120m });
        db.Bookings.AddRange(first, second);

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Ac2_Inactive_booking_seat_does_not_block_a_new_active_one()
    {
        using var db = CreateContext();
        var (showingId, seatId, otherSeatId, userId) = SeedShowing(db);
        var cancelled = NewBooking(showingId, userId, "AAAA1111");
        cancelled.Status = BookingStatus.Cancelled;
        cancelled.Seats.Add(new BookingSeat { ShowingId = showingId, SeatId = seatId, Price = 120m, IsActive = false });
        var active = NewBooking(showingId, userId, "BBBB2222");
        active.Seats.Add(new BookingSeat { ShowingId = showingId, SeatId = seatId, Price = 120m });
        active.Seats.Add(new BookingSeat { ShowingId = showingId, SeatId = otherSeatId, Price = 160m });
        db.Bookings.AddRange(cancelled, active);

        db.SaveChanges();

        Assert.Equal(3, db.BookingSeats.Count());
        Assert.Equal(2, db.BookingSeats.Count(bs => bs.IsActive));
    }

    [Fact]
    public void Ac2_Duplicate_booking_reference_throws()
    {
        using var db = CreateContext();
        var (showingId, _, _, userId) = SeedShowing(db);
        db.Bookings.AddRange(NewBooking(showingId, userId, "AAAA1111"), NewBooking(showingId, userId, "AAAA1111"));

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Ac3_Repository_supports_add_get_list_update_and_remove()
    {
        using var db = CreateContext();
        IRepository<Movie> repo = new Repository<Movie>(db);

        await repo.AddAsync(new Movie { Title = "Dune", Genre = Genre.SciFi, AgeRating = AgeRating.Age11, LengthMinutes = 155 });
        await repo.AddAsync(new Movie { Title = "Up", Genre = Genre.Animation, AgeRating = AgeRating.AllAges, LengthMinutes = 96 });
        Assert.Equal(2, await repo.SaveChangesAsync());

        var all = await repo.ListAsync();
        var animated = await repo.ListAsync(m => m.Genre == Genre.Animation);
        Assert.Equal(2, all.Count);
        Assert.Equal("Up", Assert.Single(animated).Title);
        Assert.Equal(1, repo.Query().Count(m => m.LengthMinutes > 100));

        var dune = await repo.GetByIdAsync(all.Single(m => m.Title == "Dune").Id);
        Assert.NotNull(dune);
        dune.Title = "Dune: Part One";
        repo.Update(dune);
        await repo.SaveChangesAsync();
        Assert.Equal("Dune: Part One", (await repo.GetByIdAsync(dune.Id))?.Title);

        repo.Remove(dune);
        await repo.SaveChangesAsync();
        Assert.Null(await repo.GetByIdAsync(dune.Id));
        Assert.Single(await repo.ListAsync());
    }

    [Fact]
    public async Task Ac3_Repository_honours_a_cancelled_token()
    {
        using var db = CreateContext();
        var repo = new Repository<Movie>(db);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.ListAsync(null, cts.Token));
    }

    [Fact]
    public void Ac3_Every_async_repository_method_accepts_a_cancellation_token()
    {
        var asyncMethods = typeof(IRepository<>).GetMethods()
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
            .ToList();

        Assert.Equal(4, asyncMethods.Count);
        Assert.All(asyncMethods, m =>
            Assert.Contains(m.GetParameters(), p => p.ParameterType == typeof(CancellationToken)));
    }

    [Fact]
    public void Ac3_Repository_is_registered_open_generic()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<Repository<Movie>>(scope.ServiceProvider.GetRequiredService<IRepository<Movie>>());
        Assert.IsType<Repository<Showing>>(scope.ServiceProvider.GetRequiredService<IRepository<Showing>>());
    }
}
