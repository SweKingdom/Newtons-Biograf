using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace NewtonsBiograf.Shared;

public enum Genre
{
    Action,
    Comedy,
    Drama,
    Horror,
    [Display(Name = "Sci-Fi")] SciFi,
    Animation,
    Documentary,
    Thriller
}

public enum AgeRating
{
    [Display(Name = "All ages")] AllAges,
    [Display(Name = "7")] Age7,
    [Display(Name = "11")] Age11,
    [Display(Name = "15")] Age15
}

public enum SeatType { Standard, VIP }

public enum ShowingStatus { Scheduled, Cancelled }

public enum BookingStatus { Confirmed, Cancelled }

public static class EnumExtensions
{
    /// <summary>The [Display(Name)] of an enum member, or its name when it has none.</summary>
    public static string ToDisplayName(this Enum value) =>
        value.GetType().GetField(value.ToString())?.GetCustomAttribute<DisplayAttribute>()?.Name ?? value.ToString();
}
