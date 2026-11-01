using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace NewtonsBiograf.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Auditorium> Auditoriums => Set<Auditorium>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Showing> Showings => Set<Showing>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSeat> BookingSeats => Set<BookingSeat>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>().Property(u => u.DisplayName).HasMaxLength(100);

        builder.Entity<Movie>(e =>
        {
            e.ToTable("Movie", t => t.HasCheckConstraint("CK_Movie_LengthMinutes", "\"LengthMinutes\" > 0"));
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Genre).HasConversion<string>();
            e.Property(x => x.AgeRating).HasConversion<string>();
        });

        builder.Entity<Auditorium>(e =>
        {
            e.ToTable("Auditorium");
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("IX_Auditorium_Name");
        });

        builder.Entity<Seat>(e =>
        {
            e.ToTable("Seat");
            e.Property(x => x.Type).HasConversion<string>();
            e.HasOne(x => x.Auditorium).WithMany(a => a.Seats)
                .HasForeignKey(x => x.AuditoriumId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AuditoriumId, x.RowNumber, x.SeatNumber }).IsUnique();
        });

        builder.Entity<Showing>(e =>
        {
            e.ToTable("Showing");
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Movie).WithMany().HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Auditorium).WithMany().HasForeignKey(x => x.AuditoriumId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AuditoriumId, x.StartTime });
        });

        builder.Entity<Booking>(e =>
        {
            e.ToTable("Booking");
            e.Property(x => x.Reference).HasMaxLength(8);
            e.Property(x => x.TotalPrice).HasConversion<double>();
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Showing).WithMany().HasForeignKey(x => x.ShowingId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BookingSeat>(e =>
        {
            e.ToTable("BookingSeat");
            e.Property(x => x.Price).HasConversion<double>();
            e.HasOne(x => x.Booking).WithMany(b => b.Seats)
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Seat).WithMany().HasForeignKey(x => x.SeatId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ShowingId, x.SeatId }).IsUnique().HasFilter("\"IsActive\" = 1");
        });
    }
}
