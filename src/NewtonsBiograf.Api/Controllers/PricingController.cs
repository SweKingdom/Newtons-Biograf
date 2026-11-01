using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewtonsBiograf.Api.Pricing;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

[ApiController]
[Route("api/pricing")]
public class PricingController(IPriceCalculator prices) : ControllerBase
{
    /// <summary>Current seat prices in SEK, for display to customers.</summary>
    [HttpGet]
    [AllowAnonymous]
    public PricingDto Get() => new(prices.GetPrice(SeatType.Standard), prices.GetPrice(SeatType.VIP));
}
