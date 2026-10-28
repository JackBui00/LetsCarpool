using System.ComponentModel.DataAnnotations;

namespace LetsCarpool.Server.Carpooling;

public sealed class CarpoolPlanRequest : IValidatableObject
{
    public required PlanLocation Destination { get; init; }
    public required IReadOnlyList<PlanParticipant> Participants { get; init; }
    public double MaxDetourMinutes { get; init; } = 30;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!double.IsFinite(MaxDetourMinutes) || MaxDetourMinutes is < 0 or > 1440)
            yield return new ValidationResult("Maximum detour must be between 0 and 1440 minutes.", ["maxDetourMinutes"]);
        if (Participants is null)
        {
            yield return new ValidationResult("At least one participant is required.", ["participants"]);
            yield break;
        }
        if (Participants.Count == 0)
            yield return new ValidationResult("At least one participant is required.", ["participants"]);
        if (Participants.Count > CarpoolPlanner.MaximumParticipants)
            yield return new ValidationResult($"At most {CarpoolPlanner.MaximumParticipants} participants are supported.", ["participants"]);

        foreach (var result in PlanValidation.ValidateLocation(Destination, "destination"))
            yield return result;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Participants.Count; index++)
        {
            var participant = Participants[index];
            if (participant is null)
            {
                yield return new ValidationResult("Participant is required.", [$"Participants[{index}]"]);
                continue;
            }
            var path = $"participants[{index}]";
            if (string.IsNullOrWhiteSpace(participant.Id) || participant.Id.Length > 80)
                yield return new ValidationResult("Participant id is required and must be at most 80 characters.", [$"{path}.id"]);
            else if (!ids.Add(participant.Id))
                yield return new ValidationResult("Participant ids must be unique.", [$"{path}.id"]);
            if (string.IsNullOrWhiteSpace(participant.Name) || participant.Name.Length > 120)
                yield return new ValidationResult("Name is required and must be at most 120 characters.", [$"{path}.name"]);
            if (participant.PassengerSeats is < 0 or > 8)
                yield return new ValidationResult("Passenger seats must be between 0 and 8.", [$"{path}.passengerSeats"]);
            if (participant.CanDrive == false && participant.PassengerSeats > 0)
                yield return new ValidationResult("Passenger seats can only be offered when canDrive is true.", [$"{path}.passengerSeats"]);
            if (participant.MaxDetourMinutes is { } participantDetour && (!double.IsFinite(participantDetour) || participantDetour is < 0 or > 1440))
                yield return new ValidationResult("Maximum detour must be between 0 and 1440 minutes.", [$"{path}.maxDetourMinutes"]);
            foreach (var result in PlanValidation.ValidateLocation(participant.Origin, $"{path}.origin"))
                yield return result;
        }
    }
}

public sealed record PlanLocation
{
    public required string Label { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

public sealed record PlanParticipant
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required PlanLocation Origin { get; init; }
    public bool? CanDrive { get; init; }
    public int PassengerSeats { get; init; }
    public bool CanMeet { get; init; }
    public double? MaxDetourMinutes { get; init; }
}

internal static class PlanValidation
{
    public static IEnumerable<ValidationResult> ValidateLocation(PlanLocation? location, string path)
    {
        if (location is null)
        {
            yield return new ValidationResult("Location is required.", [path]);
            yield break;
        }
        if (string.IsNullOrWhiteSpace(location.Label) || location.Label.Length > 200)
            yield return new ValidationResult("Label is required and must be at most 200 characters.", [$"{path}.label"]);
        if (!double.IsFinite(location.Latitude) || location.Latitude is < -90 or > 90)
            yield return new ValidationResult("Latitude must be between -90 and 90.", [$"{path}.latitude"]);
        if (!double.IsFinite(location.Longitude) || location.Longitude is < -180 or > 180)
            yield return new ValidationResult("Longitude must be between -180 and 180.", [$"{path}.longitude"]);
    }
}
