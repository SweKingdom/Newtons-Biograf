using Microsoft.Extensions.Options;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Pricing;

/// <summary>Seat prices in whole SEK, bound from the "Pricing" configuration section (validated on start).</summary>
public class PricingOptions
{
    public const string Section = "Pricing";

    public decimal StandardSeat { get; set; }
    public decimal VipSeat { get; set; }
}

/// <summary>The only source of seat prices; bookings store the returned price as a snapshot.</summary>
public interface IPriceCalculator
{
    decimal GetPrice(SeatType type);
}

/// <summary>Reads <see cref="IOptionsMonitor{TOptions}"/>, so edited configuration applies without a restart.</summary>
public class PriceCalculator(IOptionsMonitor<PricingOptions> options) : IPriceCalculator
{
    public decimal GetPrice(SeatType type) => type switch
    {
        SeatType.Standard => options.CurrentValue.StandardSeat,
        SeatType.VIP => options.CurrentValue.VipSeat,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown seat type.")
    };
}
