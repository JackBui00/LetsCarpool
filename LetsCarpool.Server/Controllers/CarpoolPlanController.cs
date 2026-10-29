using LetsCarpool.Server.Carpooling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LetsCarpool.Server.Controllers;

[ApiController]
[Route("api/v1/carpool/plan")]
[RequestSizeLimit(32 * 1024)]
[EnableRateLimiting("carpool-plan")]
public sealed class CarpoolPlanController(CarpoolPlanner planner) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CarpoolPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public ActionResult<CarpoolPlanResponse> Plan([FromBody] CarpoolPlanRequest request)
    {
        try
        {
            return Ok(planner.Plan(request));
        }
        catch (PlannerSearchLimitException exception)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "The exact planning search limit was exceeded.",
                Detail = $"The exact search is bounded to {exception.OperationLimit} operations. Try a smaller group or fewer offered vehicles.",
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
    }
}
