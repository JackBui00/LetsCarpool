using System.Text.Json;
using LetsCarpool.Server.Carpooling;
using Microsoft.AspNetCore.Mvc;

namespace LetsCarpool.Server.Trips;

internal sealed class TripApiExceptionMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected; do not log request data or attempt a second response.
        }
        catch (TripApiException exception) when (context.Request.Path.StartsWithSegments("/api/v1/trips"))
        {
            ProblemDetails problem;
            if (exception.Errors is not null)
            {
                problem = new ValidationProblemDetails(exception.Errors.ToDictionary(pair => pair.Key, pair => pair.Value))
                {
                    Title = exception.Title,
                    Detail = exception.Detail,
                    Status = exception.Status
                };
            }
            else
            {
                problem = new ProblemDetails
                {
                    Title = exception.Title,
                    Detail = exception.Detail,
                    Status = exception.Status
                };
            }
            AddExtensions(problem, exception.Code, exception.ExpectedRevision, exception.CurrentRevision);
            await WriteProblemAsync(context, problem);
        }
        catch (PlannerSearchLimitException exception) when (context.Request.Path.StartsWithSegments("/api/v1/trips"))
        {
            await WriteProblemAsync(context, new ProblemDetails
            {
                Title = "The exact planning search limit was exceeded.",
                Detail = $"The exact search is bounded to {exception.OperationLimit} operations. Try fewer participants or fewer offered vehicles.",
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
        catch (Exception) when (context.Request.Path.StartsWithSegments("/api/v1/trips"))
        {
            await WriteProblemAsync(context, new ProblemDetails
            {
                Title = "The trip request could not be completed.",
                Detail = "Retry the request. If the problem continues, contact the local trip host.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    private static void AddExtensions(ProblemDetails problem, string? code, long? expected, long? current)
    {
        if (code is not null) problem.Extensions["code"] = code;
        if (expected is not null) problem.Extensions["expectedRevision"] = expected;
        if (current is not null) problem.Extensions["currentRevision"] = current;
    }

    private static async Task WriteProblemAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.Clear();
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, JsonOptions, context.RequestAborted);
    }
}
