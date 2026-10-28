using LetsCarpool.Server.Carpooling;
using Microsoft.AspNetCore.Mvc;

namespace LetsCarpool.Server.Controllers;

[ApiController]
[Route("api/v1/carpool/plan")]
public sealed class CarpoolPlanController(CarpoolPlanner planner) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CarpoolPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<CarpoolPlanResponse> Plan([FromBody] CarpoolPlanRequest request) => Ok(planner.Plan(request));
}
