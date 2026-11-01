using NewtonsBiograf.Api.Services;
using NewtonsBiograf.Tests.Infrastructure;

namespace NewtonsBiograf.Tests.Scenarios;

/// <summary>US-141 AC 4: showings in one auditorium may not overlap; the 15 minute cleaning time counts as part of a showing.</summary>
[Collection("Scenarios")]
public sealed class ShowingOverlapScenarios : IDisposable
{
    private static readonly DateTime ExistingEnd = TestDb.Start.AddMinutes(TestDb.MovieMinutes + TestDb.CleaningMinutes);

    private readonly TestDb _db = TestDb.InMemory();
    private readonly ApiFactory _api;

    public ShowingOverlapScenarios()
    {
        _api = new ApiFactory(_db, new FakeClock());
        _db.AddShowing(TestDb.Start); // Dune in Salong 1
    }

    public void Dispose()
    {
        _api.Dispose();
        _db.Dispose();
    }

    private Task<SlotResult> Validate(DateTime start, int auditoriumId) =>
        _api.With<ShowingService, SlotResult>(s => s.ValidateSlotAsync(
            new ShowingRequest { MovieId = _db.MovieId, AuditoriumId = auditoriumId, StartTime = start }, null));

    [Fact]
    public async Task Overlapping_showing_is_rejected_and_names_the_conflicting_showing()
    {
        var result = await Validate(TestDb.Start.AddMinutes(60), _db.Salong1);

        var conflict = Assert.IsType<SlotConflict>(result);
        // 18:00 UTC on 10 March 2040 is 19:00 in Stockholm; the showing runs 115 minutes.
        Assert.Contains("'Dune' 19:00–20:55 in Salong 1", conflict.Detail);
    }

    [Fact]
    public async Task Showing_starting_one_minute_before_the_previous_one_ends_is_rejected()
    {
        Assert.IsType<SlotConflict>(await Validate(ExistingEnd.AddMinutes(-1), _db.Salong1));
    }

    [Fact]
    public async Task Back_to_back_showing_starting_when_the_previous_one_ends_including_cleaning_is_accepted()
    {
        var result = await Validate(ExistingEnd, _db.Salong1);

        var ok = Assert.IsType<SlotOk>(result);
        Assert.Equal(ExistingEnd, ok.Start);
        Assert.Equal(ExistingEnd.AddMinutes(TestDb.MovieMinutes + TestDb.CleaningMinutes), ok.End);
    }

    [Fact]
    public async Task Showing_at_the_same_time_in_another_auditorium_is_accepted()
    {
        Assert.IsType<SlotOk>(await Validate(TestDb.Start, _db.Salong2));
    }
}
