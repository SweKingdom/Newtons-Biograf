using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<HealthResponse> Get() => new HealthResponse("ok");
}
