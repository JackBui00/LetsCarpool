using System.ComponentModel.DataAnnotations;
using LetsCarpool.Server.Carpooling;

namespace LetsCarpool.Server.Trips;

public sealed class TripService
{
    private const int MaximumJoinedParticipants = 6;
    private static readonly TimeSpan ParticipantAccessLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan OrganizerAccessLifetime = TimeSpan.FromDays(90);
    private readonly ITripStore store;
    private readonly CarpoolPlanner planner;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan inviteLifetime;

    internal TripService(ITripStore store, CarpoolPlanner planner, IConfiguration configuration, TimeProvider timeProvider)
    {
        this.store = store;
        this.planner = planner;
        this.timeProvider = timeProvider;
        var configuredDays = int.TryParse(configuration["CarpoolStore:InviteLifetimeDays"], out var days) ? days : 7;
        inviteLifetime = TimeSpan.FromDays(Math.Clamp(configuredDays, 1, 30));
    }

    public async Task<CreateTripResponse> CreateAsync(CreateTripRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw Validation("name", "Trip name is required.");
        var arrival = ParseArrival(request.ArrivalAt, request.TimeZoneId);
        var now = timeProvider.GetUtcNow();
        var organizerSecret = CapabilityTokens.Generate();
        var inviteSecret = CapabilityTokens.Generate();
        var inviteExpiry = now.Add(inviteLifetime);
        var organizerExpiry = OwnerExpiry(now, arrival);
        var trip = new StoredTrip
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.Name.Trim(),
            ArrivalAt = arrival,
            TimeZoneId = request.TimeZoneId.Trim(),
            Destination = request.Destination,
            Revision = 1,
            CreatedAt = now,
            InviteExpiresAt = inviteExpiry,
            Capabilities =
            [
                CreateCapability(organizerSecret, "organizer", null, organizerExpiry),
                CreateCapability(inviteSecret, "invite", null, inviteExpiry)
            ]
        };
        await store.MutateAsync(database =>
        {
            database.Trips.Add(trip);
            return true;
        }, cancellationToken);
        return new CreateTripResponse(trip.Id, trip.Revision, organizerSecret, organizerExpiry, inviteSecret, inviteExpiry);
    }

    public Task<InviteTripView> GetInviteAsync(string tripId, string? token, CancellationToken cancellationToken) =>
        store.ReadAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            RequireCapability(trip, token, "invite", null);
            return new InviteTripView(trip.Id, trip.Name, trip.ArrivalAt, trip.TimeZoneId, trip.Destination, trip.InviteExpiresAt);
        }, cancellationToken);

    public async Task<JoinTripResponse> JoinAsync(string tripId, string? token, string idempotencyKey, JoinTripRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(idempotencyKey, out var parsedKey))
            throw Validation("idempotencyKey", "Idempotency-Key must be a UUID.");
        var key = parsedKey.ToString("N");
        var now = timeProvider.GetUtcNow();
        var participantId = Guid.NewGuid().ToString("N");
        var participantSecret = CapabilityTokens.Generate();
        var result = await store.MutateAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            var inviteCapability = RequireCapability(trip, token, "invite", null);
            var scopedKey = inviteCapability.TokenHash + ":" + key;
            if (trip.JoinRequests.Any(join => join.Key == scopedKey))
                throw new TripApiException(409, "Join already processed", "This join request already completed and will not create another participant. Continue with the participant capability saved in this browser. If none was saved, access cannot be recovered without an account.", "join_already_processed");
            if (trip.Participants.Count >= MaximumJoinedParticipants)
                throw new TripApiException(409, "Trip is full", "This trip already has the maximum of six joined participants.", "trip_capacity_reached");
            if (string.IsNullOrWhiteSpace(request.Name)) throw Validation("name", "Name is required.");

            trip.Participants.Add(new StoredParticipant
            {
                Id = participantId,
                Name = request.Name.Trim(),
                Origin = request.Origin,
                CanDrive = request.CanDrive,
                PassengerSeats = request.PassengerSeats,
                CanMeet = request.CanMeet,
                MaxDetourMinutes = request.MaxDetourMinutes,
                LocationDisclosureAccepted = request.LocationDisclosureAccepted,
                JoinedAt = now
            });
            trip.Capabilities.Add(CreateCapability(participantSecret, "participant", participantId, trip.ArrivalAt.Add(ParticipantAccessLifetime)));
            trip.JoinRequests.Add(new StoredJoinRequest { Key = scopedKey, ParticipantId = participantId });
            InvalidatePlan(trip);
            return new JoinTripResponse(participantId, participantSecret, trip.Revision);
        }, cancellationToken);
        return result;
    }

    public Task<OrganizerTripView> GetOrganizerAsync(string tripId, string? token, CancellationToken cancellationToken) =>
        store.ReadAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            RequireCapability(trip, token, "organizer", null);
            return ToOrganizerView(trip);
        }, cancellationToken);

    public async Task<OrganizerTripView> UpdateTripAsync(string tripId, string? token, UpdateTripRequest request, CancellationToken cancellationToken)
    {
        var updated = await store.MutateAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            RequireCapability(trip, token, "organizer", null);
            RequireRevision(trip, request.ExpectedRevision);
            var name = request.Name?.Trim() ?? trip.Name;
            if (name.Length == 0) throw Validation("name", "Trip name is required.");
            var timeZoneId = request.TimeZoneId?.Trim() ?? trip.TimeZoneId;
            var arrival = request.ArrivalAt is null ? trip.ArrivalAt : ParseArrival(request.ArrivalAt, timeZoneId);
            ValidateTimeZone(arrival, timeZoneId);
            trip.Name = name;
            trip.TimeZoneId = timeZoneId;
            trip.ArrivalAt = arrival;
            trip.Destination = request.Destination ?? trip.Destination;
            ExtendAccessExpiry(trip, arrival);
            InvalidatePlan(trip);
            return ToOrganizerView(trip);
        }, cancellationToken);
        return updated;
    }

    public Task<ParticipantTripView> GetParticipantAsync(string tripId, string? token, CancellationToken cancellationToken) =>
        store.ReadAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            var capability = RequireParticipantCapability(trip, token);
            var participant = trip.Participants.SingleOrDefault(person => person.Id == capability.ParticipantId)
                ?? throw AccessUnavailable();
            var self = ToParticipantView(participant);
            if (trip.CurrentPlan is null || trip.CurrentPlan.BasedOnRevision != trip.Revision || trip.CurrentPlan.SelectedStrategy is null)
                return new ParticipantTripView(trip.Id, trip.Name, trip.ArrivalAt, trip.TimeZoneId, trip.Destination, trip.InviteExpiresAt, trip.Revision, self, null, null, trip.PlanStale);

            var alternative = trip.CurrentPlan.Result.Alternatives.SingleOrDefault(item => item.Strategy == trip.CurrentPlan.SelectedStrategy);
            var group = alternative?.Groups.SingleOrDefault(item => item.Driver.Id == participant.Id || item.Riders.Any(rider => rider.Id == participant.Id));
            if (group is not null)
            {
                var assignment = new ParticipantAssignment(trip.CurrentPlan.SelectedStrategy,
                    group.Driver.Id == participant.Id ? "driver" : "rider", group.Driver, group.Riders, group.Stops,
                    group.TotalDistanceMeters, group.VehicleDistanceMeters, group.IndependentTravelDistanceMeters, group.DurationMinutes,
                    group.IndependentTravelLegs.Where(leg => leg.ParticipantId == participant.Id).ToArray());
                return new ParticipantTripView(trip.Id, trip.Name, trip.ArrivalAt, trip.TimeZoneId, trip.Destination, trip.InviteExpiresAt, trip.Revision, self, assignment, null, false);
            }
            var reason = alternative?.Unassigned.SingleOrDefault(item => item.ParticipantId == participant.Id)?.Reason;
            return new ParticipantTripView(trip.Id, trip.Name, trip.ArrivalAt, trip.TimeZoneId, trip.Destination, trip.InviteExpiresAt, trip.Revision, self, null, reason, false);
        }, cancellationToken);

    public Task<CurrentRevisionResponse> UpdateParticipantAsync(string tripId, string? token, UpdateParticipantRequest request, CancellationToken cancellationToken) =>
        store.MutateAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            var capability = RequireParticipantCapability(trip, token);
            RequireRevision(trip, request.ExpectedRevision);
            var participant = trip.Participants.SingleOrDefault(person => person.Id == capability.ParticipantId)
                ?? throw AccessUnavailable();
            if (string.IsNullOrWhiteSpace(request.Name)) throw Validation("name", "Name is required.");
            if (!participant.LocationDisclosureAccepted && request.LocationDisclosureAccepted != true)
                throw Validation("locationDisclosureAccepted", "Accept the location-sharing disclosure before saving this profile.");
            participant.Name = request.Name.Trim();
            participant.Origin = request.Origin;
            participant.CanDrive = request.CanDrive;
            participant.PassengerSeats = request.PassengerSeats;
            participant.CanMeet = request.CanMeet;
            participant.MaxDetourMinutes = request.MaxDetourMinutes;
            if (request.LocationDisclosureAccepted == true) participant.LocationDisclosureAccepted = true;
            InvalidatePlan(trip);
            return new CurrentRevisionResponse(trip.Revision);
        }, cancellationToken);

    public async Task<RotateInviteResponse> RotateInviteAsync(string tripId, string? token, CancellationToken cancellationToken)
    {
        var newSecret = CapabilityTokens.Generate();
        var expiry = timeProvider.GetUtcNow().Add(inviteLifetime);
        await store.MutateAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            RequireCapability(trip, token, "organizer", null);
            RevokePurpose(trip, "invite", timeProvider.GetUtcNow());
            trip.InviteExpiresAt = expiry;
            trip.Capabilities.Add(CreateCapability(newSecret, "invite", null, expiry));
            return true;
        }, cancellationToken);
        return new RotateInviteResponse(newSecret, expiry);
    }

    public Task<CurrentRevisionResponse> RevokeInviteAsync(string tripId, string? token, CancellationToken cancellationToken) =>
        store.MutateAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            RequireCapability(trip, token, "organizer", null);
            var now = timeProvider.GetUtcNow();
            RevokePurpose(trip, "invite", now);
            trip.InviteExpiresAt = now;
            return new CurrentRevisionResponse(trip.Revision);
        }, cancellationToken);

    public async Task<RotateOrganizerResponse> RotateOrganizerAsync(string tripId, string? token, CancellationToken cancellationToken)
    {
        var newSecret = CapabilityTokens.Generate();
        var now = timeProvider.GetUtcNow();
        var expiry = now.Add(OrganizerAccessLifetime);
        await store.MutateAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            RequireCapability(trip, token, "organizer", null);
            RevokePurpose(trip, "organizer", now);
            var ownerExpiry = OwnerExpiry(now, trip.ArrivalAt);
            trip.Capabilities.Add(CreateCapability(newSecret, "organizer", null, ownerExpiry));
            expiry = ownerExpiry;
            return true;
        }, cancellationToken);
        return new RotateOrganizerResponse(newSecret, expiry);
    }

    public async Task<GenerateTripPlanResponse> GeneratePlanAsync(string tripId, string? token, long expectedRevision, CancellationToken cancellationToken)
    {
        var generation = await store.MutateAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            RequireCapability(trip, token, "organizer", null);
            RequireRevision(trip, expectedRevision);
            if (trip.Participants.Count == 0)
                throw new TripApiException(409, "No joined participants", "Join at least one participant before generating a plan.", "no_participants");
            trip.PlanGenerationId = (trip.PlanGenerationId ?? 0) + 1;
            return new PlanGenerationSnapshot(trip.Id, trip.Revision, trip.PlanGenerationId.Value, trip.Destination,
                trip.Participants.Select(person => new PlanParticipant
                {
                    Id = person.Id,
                    Name = person.Name,
                    Origin = person.Origin,
                    CanDrive = person.CanDrive,
                    PassengerSeats = person.PassengerSeats,
                    CanMeet = person.CanMeet,
                    MaxDetourMinutes = person.MaxDetourMinutes
                }).ToArray());
        }, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var result = planner.Plan(new CarpoolPlanRequest
        {
            Destination = generation.Destination,
            Participants = generation.Participants,
            MaxDetourMinutes = 30
        });
        var planId = Guid.NewGuid().ToString("N");
        return await store.MutateAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            RequireCapability(trip, token, "organizer", null);
            RequireRevision(trip, generation.Revision);
            if (trip.PlanGenerationId != generation.GenerationId)
                throw new TripApiException(409, "Plan generation superseded", "A newer plan request replaced this result.", "revision_conflict", generation.Revision, trip.Revision);
            trip.CurrentPlan = new StoredPlan { Id = planId, BasedOnRevision = generation.Revision, Result = result };
            trip.PlanStale = false;
            return new GenerateTripPlanResponse(planId, generation.Revision, result);
        }, cancellationToken);
    }

    public Task<CurrentRevisionResponse> SelectPlanAsync(string tripId, string planId, string? token, long expectedRevision, string strategy, CancellationToken cancellationToken) =>
        store.MutateAsync(database =>
        {
            var trip = FindTrip(database, tripId);
            RequireCapability(trip, token, "organizer", null);
            RequireRevision(trip, expectedRevision);
            if (trip.CurrentPlan is null || trip.CurrentPlan.Id != planId || trip.CurrentPlan.BasedOnRevision != trip.Revision)
                throw new TripApiException(409, "Plan is stale", "Generate a plan for the current trip revision before selecting it.", "stale_plan", expectedRevision, trip.Revision);
            if (!trip.CurrentPlan.Result.Alternatives.Any(alternative => alternative.Strategy == strategy))
                throw new TripApiException(400, "Invalid plan selection", "Choose one of the returned plan strategies.", "validation_error");
            trip.CurrentPlan.SelectedStrategy = strategy;
            return new CurrentRevisionResponse(trip.Revision);
        }, cancellationToken);

    private static OrganizerTripView ToOrganizerView(StoredTrip trip) => new(
        trip.Id, trip.Name, trip.ArrivalAt, trip.TimeZoneId, trip.Destination, trip.InviteExpiresAt, trip.Revision,
        trip.Participants.Select(ToParticipantView).ToArray(), trip.CurrentPlan?.SelectedStrategy,
        trip.CurrentPlan is null ? null : new GeneratedTripPlanView(trip.CurrentPlan.Id, trip.CurrentPlan.BasedOnRevision, trip.CurrentPlan.Result),
        trip.PlanStale);

    private static TripParticipantView ToParticipantView(StoredParticipant participant) => new(
        participant.Id, participant.Name, participant.Origin, participant.CanDrive, participant.PassengerSeats,
        participant.CanMeet, participant.MaxDetourMinutes, participant.LocationDisclosureAccepted, participant.JoinedAt);

    private static StoredTrip FindTrip(TripDatabase database, string tripId)
    {
        if (Guid.TryParseExact(tripId, "N", out _) && database.Trips.SingleOrDefault(trip => trip.Id == tripId) is { } trip)
            return trip;
        throw AccessUnavailable();
    }

    private StoredCapability RequireParticipantCapability(StoredTrip trip, string? token)
    {
        var capability = FindCapability(trip, token, "participant");
        if (capability?.ParticipantId is null) throw AccessUnavailable();
        return capability;
    }

    private StoredCapability RequireCapability(StoredTrip trip, string? token, string purpose, string? participantId)
    {
        var capability = FindCapability(trip, token, purpose);
        if (capability is null || capability.ParticipantId != participantId) throw AccessUnavailable();
        return capability;
    }

    private StoredCapability? FindCapability(StoredTrip trip, string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var now = timeProvider.GetUtcNow();
        StoredCapability? matched = null;
        foreach (var capability in trip.Capabilities)
        {
            var tokenMatches = CapabilityTokens.Matches(token, capability.TokenHash);
            if (tokenMatches && capability.Purpose == purpose && capability.RevokedAt is null && capability.ExpiresAt > now)
                matched = capability;
        }
        return matched;
    }

    private static void RequireRevision(StoredTrip trip, long expectedRevision)
    {
        if (trip.Revision != expectedRevision)
            throw new TripApiException(409, "Trip revision changed", "Refresh the trip and retry using its current revision.", "revision_conflict", expectedRevision, trip.Revision);
    }

    private static void InvalidatePlan(StoredTrip trip)
    {
        trip.Revision++;
        trip.PlanGenerationId = (trip.PlanGenerationId ?? 0) + 1;
        if (trip.CurrentPlan is not null) trip.PlanStale = true;
        trip.CurrentPlan = null;
    }

    private static StoredCapability CreateCapability(string token, string purpose, string? participantId, DateTimeOffset expiresAt) => new()
    {
        TokenHash = CapabilityTokens.Hash(token),
        Purpose = purpose,
        ParticipantId = participantId,
        ExpiresAt = expiresAt
    };

    private static void RevokePurpose(StoredTrip trip, string purpose, DateTimeOffset now)
    {
        foreach (var capability in trip.Capabilities.Where(item => item.Purpose == purpose && item.RevokedAt is null))
            capability.RevokedAt = now;
    }

    private void ExtendAccessExpiry(StoredTrip trip, DateTimeOffset arrival)
    {
        var now = timeProvider.GetUtcNow();
        var participantExpiry = arrival.Add(ParticipantAccessLifetime);
        var organizerExpiry = OwnerExpiry(now, arrival);
        foreach (var capability in trip.Capabilities.Where(item => item.RevokedAt is null && item.ExpiresAt > now))
        {
            if (capability.Purpose == "participant") capability.ExpiresAt = participantExpiry;
            else if (capability.Purpose == "organizer") capability.ExpiresAt = organizerExpiry;
        }
    }

    private DateTimeOffset ParseArrival(string value, string timeZoneId)
    {
        if (!TripValidation.TryParseArrival(value, out var arrival) || arrival <= timeProvider.GetUtcNow())
            throw Validation("arrivalAt", "Arrival time must be a future ISO 8601 timestamp with an explicit UTC offset.");
        ValidateTimeZone(arrival, timeZoneId);
        return arrival;
    }

    private static void ValidateTimeZone(DateTimeOffset arrival, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > 100 || !TripValidation.MatchesTimeZone(arrival, timeZoneId))
            throw Validation("timeZoneId", "Provide a valid IANA time zone whose offset matches arrivalAt.");
    }

    private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private DateTimeOffset OwnerExpiry(DateTimeOffset now, DateTimeOffset arrival) => Later(now.Add(OrganizerAccessLifetime), arrival.Add(ParticipantAccessLifetime));

    private static TripApiException Validation(string field, string message) =>
        new(400, "One or more validation errors occurred.", message, "validation_error", errors: new Dictionary<string, string[]> { [field] = [message] });

    private static TripApiException AccessUnavailable() =>
        new(404, "Access unavailable", "This trip link is invalid, expired, revoked, or does not have permission for this action.", "access_unavailable");

    private sealed record PlanGenerationSnapshot(string TripId, long Revision, long GenerationId, PlanLocation Destination, PlanParticipant[] Participants);
}
