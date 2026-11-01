using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewtonsBiograf.Api.Data;

namespace NewtonsBiograf.Tests.Infrastructure;

/// <summary>A clock the test moves by hand. The API reads time through <see cref="TimeProvider"/>.</summary>
public sealed class FakeClock : TimeProvider
{
    public DateTime UtcNow { get; set; } = TestDb.Start.AddDays(-1);

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
}

/// <summary>
/// A real, migrated SQLite database seeded with two auditoriums, one movie and two customers, so unique indexes
/// really apply. <see cref="InMemory"/> keeps one open connection; <see cref="File"/> uses a temporary file so that
/// parallel contexts truly race, and deletes it on dispose.
/// </summary>
public sealed class TestDb : IDisposable
{
    /// <summary>Fixed showing start, far in the future so the API's own "start must be in the future" check passes.</summary>
    public static readonly DateTime Start = new(2040, 3, 10, 18, 0, 0, DateTimeKind.Utc);

    public const string CustomerA = "customer-a";
    public const string CustomerB = "customer-b";
    public const int MovieMinutes = 100;
    public const int CleaningMinutes = 15;
    public const decimal StandardPrice = 140;

    private readonly SqliteConnection? _connection;
    private readonly string? _path;

    private TestDb(SqliteConnection? connection, string? path)
    {
        (_connection, _path) = (connection, path);
        Seed();
    }

    /// <summary>Salong 1 has three Standard seats (row 1) and one VIP seat (row 2); Salong 2 is empty of seats.</summary>
    public int Salong1 { get; private set; }

    public int Salong2 { get; private set; }

    public int MovieId { get; private set; }

    public int[] Standard { get; private set; } = [];

    public int Vip { get; private set; }

    public static TestDb InMemory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return new TestDb(connection, null);
    }

    public static TestDb File() => new(null, Path.Combine(Path.GetTempPath(), $"nb-scenario-{Guid.NewGuid():N}.db"));

    public void Configure(DbContextOptionsBuilder options)
    {
        if (_connection is not null)
        {
            options.UseSqlite(_connection);
        }
        else
        {
            options.UseSqlite($"Data Source={_path}");
        }
    }

    public AppDbContext NewContext()
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        Configure(builder);
        return new AppDbContext(builder.Options);
    }

    /// <summary>A scheduled showing of the seeded movie; it ends after the film plus cleaning time.</summary>
    public int AddShowing(DateTime start, int? auditoriumId = null)
    {
        using var db = NewContext();
        var showing = new Showing
        {
            MovieId = MovieId, AuditoriumId = auditoriumId ?? Salong1, StartTime = start,
            EndTime = start.AddMinutes(MovieMinutes + CleaningMinutes)
        };
        db.Showings.Add(showing);
        db.SaveChanges();
        return showing.Id;
    }

    /// <summary>A Confirmed booking with active seats at <see cref="StandardPrice"/> each, written straight to the database.</summary>
    public int AddBooking(int showingId, string userId, params int[] seatIds)
    {
        using var db = NewContext();
        var booking = new Booking
        {
            UserId = userId, ShowingId = showingId, Reference = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            Status = BookingStatus.Confirmed, TotalPrice = StandardPrice * seatIds.Length, CreatedAt = Start.AddDays(-5),
            Seats = seatIds.Select(id => new BookingSeat { SeatId = id, ShowingId = showingId, Price = StandardPrice }).ToList()
        };
        db.Bookings.Add(booking);
        db.SaveChanges();
        return booking.Id;
    }

    public Booking GetBooking(int id)
    {
        using var db = NewContext();
        return db.Bookings.Include(b => b.Seats).Single(b => b.Id == id);
    }

    public (int Bookings, int Seats) Counts()
    {
        using var db = NewContext();
        return (db.Bookings.Count(), db.BookingSeats.Count());
    }

    public void Dispose()
    {
        _connection?.Dispose();
        if (_path is not null)
        {
            SqliteConnection.ClearAllPools();
            System.IO.File.Delete(_path);
        }
    }

    private void Seed()
    {
        using var db = NewContext();
        db.Database.Migrate();
        var salong1 = new Auditorium { Name = "Salong 1", Rows = 2, SeatsPerRow = 3 };
        var salong2 = new Auditorium { Name = "Salong 2", Rows = 1, SeatsPerRow = 1 };
        var standard = Enumerable.Range(1, 3)
            .Select(n => new Seat { Auditorium = salong1, RowNumber = 1, SeatNumber = n, Type = SeatType.Standard }).ToArray();
        var vip = new Seat { Auditorium = salong1, RowNumber = 2, SeatNumber = 1, Type = SeatType.VIP };
        var movie = new Movie { Title = "Dune", Genre = Genre.SciFi, AgeRating = AgeRating.Age11, LengthMinutes = MovieMinutes };
        db.AddRange(salong1, salong2, movie, vip);
        db.AddRange(standard);
        foreach (var id in new[] { CustomerA, CustomerB })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = $"{id}@example.com", Email = $"{id}@example.com", DisplayName = id });
        }
        db.SaveChanges();
        (Salong1, Salong2, MovieId, Vip) = (salong1.Id, salong2.Id, movie.Id, vip.Id);
        Standard = standard.Select(s => s.Id).ToArray();
    }
}
