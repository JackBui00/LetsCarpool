using LetsCarpool.Server.Trips;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LetsCarpool.Server.Controllers;

[ApiController]
[Route("api/v1/trips")]
[RequestSizeLimit(32 * 1024)]
public sealed class TripsController(TripService trips) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("trip-create")]
    [ProducesResponseType<CreateTripResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CreateTripResponse>> Create([FromBody] CreateTripRequest request, CancellationToken cancellationToken)
    {
        var created = await trips.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/trips/{created.TripId}/organizer", created);
    }

    [HttpGet("{tripId}/invite")]
    [ProducesResponseType<InviteTripView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<InviteTripView> Invite(string tripId, CancellationToken cancellationToken) =>
        trips.GetInviteAsync(tripId, BearerToken(), cancellationToken);

    [HttpPost("{tripId}/participants")]
    [EnableRateLimiting("trip-join")]
    [ProducesResponseType<JoinTripResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<JoinTripResponse>> Join(string tripId, [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromBody] JoinTripRequest request, CancellationToken cancellationToken)
    {
        var joined = await trips.JoinAsync(tripId, BearerToken(), idempotencyKey, request, cancellationToken);
        return Created($"/api/v1/trips/{tripId}/participants/me", joined);
    }

    [HttpGet("{tripId}/organizer")]
    [ProducesResponseType<OrganizerTripView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<OrganizerTripView> Organizer(string tripId, CancellationToken cancellationToken) =>
        trips.GetOrganizerAsync(tripId, BearerToken(), cancellationToken);

    [HttpPatch("{tripId}/organizer")]
    [ProducesResponseType<OrganizerTripView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<OrganizerTripView> UpdateOrganizer(string tripId, [FromBody] UpdateTripRequest request, CancellationToken cancellationToken) =>
        trips.UpdateTripAsync(tripId, BearerToken(), request, cancellationToken);

    [HttpPost("{tripId}/invite/rotate")]
    [ProducesResponseType<RotateInviteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RotateInviteResponse> RotateInvite(string tripId, CancellationToken cancellationToken) =>
        trips.RotateInviteAsync(tripId, BearerToken(), cancellationToken);

    [HttpPost("{tripId}/invite/revoke")]
    [ProducesResponseType<CurrentRevisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CurrentRevisionResponse> RevokeInvite(string tripId, CancellationToken cancellationToken) =>
        trips.RevokeInviteAsync(tripId, BearerToken(), cancellationToken);

    [HttpPost("{tripId}/organizer/rotate")]
    [ProducesResponseType<RotateOrganizerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RotateOrganizerResponse> RotateOrganizer(string tripId, CancellationToken cancellationToken) =>
        trips.RotateOrganizerAsync(tripId, BearerToken(), cancellationToken);

    [HttpGet("{tripId}/participants/me")]
    [ProducesResponseType<ParticipantTripView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ParticipantTripView> Participant(string tripId, CancellationToken cancellationToken) =>
        trips.GetParticipantAsync(tripId, BearerToken(), cancellationToken);

    [HttpPut("{tripId}/participants/me")]
    [ProducesResponseType<CurrentRevisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CurrentRevisionResponse> UpdateParticipant(string tripId, [FromBody] UpdateParticipantRequest request, CancellationToken cancellationToken) =>
        trips.UpdateParticipantAsync(tripId, BearerToken(), request, cancellationToken);

    [HttpPost("{tripId}/plans")]
    [ProducesResponseType<GenerateTripPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<GenerateTripPlanResponse> GeneratePlan(string tripId, [FromBody] RevisionRequest request, CancellationToken cancellationToken) =>
        trips.GeneratePlanAsync(tripId, BearerToken(), request.ExpectedRevision, cancellationToken);

    [HttpPost("{tripId}/plans/{planId}/select")]
    [ProducesResponseType<CurrentRevisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CurrentRevisionResponse> SelectPlan(string tripId, string planId, [FromBody] SelectTripPlanRequest request, CancellationToken cancellationToken) =>
        trips.SelectPlanAsync(tripId, planId, BearerToken(), request.ExpectedRevision, request.Strategy, cancellationToken);

    private string? BearerToken()
    {
        var value = Request.Headers["Authorization"].ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? value[7..].Trim() : null;
    }
}

public sealed class RevisionRequest
{
    [System.ComponentModel.DataAnnotations.Range(1, long.MaxValue)] public long ExpectedRevision { get; init; }
}
