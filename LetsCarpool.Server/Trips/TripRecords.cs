using LetsCarpool.Server.Carpooling;

namespace LetsCarpool.Server.Trips;

internal sealed class TripDatabase
{
    public TripDatabase() { }
    public int SchemaVersion { get; set; } = 2;
    public List<StoredTrip> Trips { get; set; } = [];
}

internal sealed class StoredTrip
{
    public StoredTrip() { }
    public required string Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset ArrivalAt { get; set; }
    public required string TimeZoneId { get; set; }
    public required TripLocation Destination { get; set; }
    public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset InviteExpiresAt { get; set; }
    public List<StoredCapability> Capabilities { get; set; } = [];
    public List<StoredParticipant> Participants { get; set; } = [];
    public List<StoredJoinRequest> JoinRequests { get; set; } = [];
    public StoredPlan? CurrentPlan { get; set; }
    public bool PlanStale { get; set; }
    public long? PlanGenerationId { get; set; }
}

internal sealed class StoredCapability
{
    public StoredCapability() { }
    public required string TokenHash { get; set; }
    public required string Purpose { get; set; }
    public string? ParticipantId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class StoredParticipant
{
    public StoredParticipant() { }
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required TripLocation Origin { get; set; }
    public bool CanDrive { get; set; }
    public int PassengerSeats { get; set; }
    public bool CanMeet { get; set; }
    public double MaxDetourMinutes { get; set; }
    public bool LocationDisclosureAccepted { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

internal sealed class StoredJoinRequest
{
    public StoredJoinRequest() { }
    public required string Key { get; set; }
    public required string ParticipantId { get; set; }
}

internal sealed class StoredPlan
{
    public StoredPlan() { }
    public required string Id { get; set; }
    public long BasedOnRevision { get; set; }
    public required CarpoolPlanResponse Result { get; set; }
    public string? SelectedStrategy { get; set; }
}
