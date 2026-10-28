using System.ComponentModel.DataAnnotations;
using LetsCarpool.Server.Carpooling;

namespace LetsCarpool.Server.Trips;

public sealed class CreateTripRequest : IValidatableObject
{
    [Required, StringLength(120, MinimumLength = 1)] public required string Name { get; init; }
    [Required] public required string ArrivalAt { get; init; }
    [Required, StringLength(100)] public required string TimeZoneId { get; init; }
    [Required] public required PlanLocation Destination { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return new ValidationResult("Trip name is required.", ["name"]);
        if (!TripValidation.TryParseArrival(ArrivalAt, out var arrival) || arrival <= DateTimeOffset.UtcNow)
            yield return new ValidationResult("Arrival time must be a future ISO 8601 timestamp with an explicit UTC offset.", ["arrivalAt"]);
        foreach (var error in PlanValidation.ValidateLocation(Destination, "destination")) yield return error;
    }
}

public sealed class JoinTripRequest : IValidatableObject
{
    [Required, StringLength(120, MinimumLength = 1)] public required string Name { get; init; }
    [Required] public required PlanLocation Origin { get; init; }
    public bool CanDrive { get; init; }
    public int PassengerSeats { get; init; }
    public bool CanMeet { get; init; }
    public double MaxDetourMinutes { get; init; } = 30;
    public bool LocationDisclosureAccepted { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return new ValidationResult("Name is required.", ["name"]);
        if (PassengerSeats is < 0 or > 8)
            yield return new ValidationResult("Passenger seats must be between 0 and 8.", ["passengerSeats"]);
        if (!CanDrive && PassengerSeats > 0)
            yield return new ValidationResult("Passenger seats can only be offered when canDrive is true.", ["passengerSeats"]);
        if (!double.IsFinite(MaxDetourMinutes) || MaxDetourMinutes is < 0 or > 1440)
            yield return new ValidationResult("Maximum detour must be between 0 and 1440 minutes.", ["maxDetourMinutes"]);
        if (!LocationDisclosureAccepted)
            yield return new ValidationResult("Confirm that your selected location may be shared with the organizer and assigned carpool companions.", ["locationDisclosureAccepted"]);
        foreach (var error in PlanValidation.ValidateLocation(Origin, "origin")) yield return error;
    }
}

public sealed class UpdateTripRequest : IValidatableObject
{
    public long ExpectedRevision { get; init; }
    [StringLength(120, MinimumLength = 1)] public string? Name { get; init; }
    public string? ArrivalAt { get; init; }
    [StringLength(100)] public string? TimeZoneId { get; init; }
    public PlanLocation? Destination { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && string.IsNullOrWhiteSpace(Name)) yield return new ValidationResult("Trip name cannot be empty.", ["name"]);
        if (ExpectedRevision < 1) yield return new ValidationResult("Expected revision must be positive.", ["expectedRevision"]);
        if (Name is null && ArrivalAt is null && TimeZoneId is null && Destination is null)
            yield return new ValidationResult("At least one trip field must be updated.", ["name", "arrivalAt", "timeZoneId", "destination"]);
        if (ArrivalAt is not null && (!TripValidation.TryParseArrival(ArrivalAt, out var arrival) || arrival <= DateTimeOffset.UtcNow))
            yield return new ValidationResult("Arrival time must be a future ISO 8601 timestamp with an explicit UTC offset.", ["arrivalAt"]);
        if (Destination is not null)
            foreach (var error in PlanValidation.ValidateLocation(Destination, "destination")) yield return error;
    }
}

public sealed class UpdateParticipantRequest : IValidatableObject
{
    public long ExpectedRevision { get; init; }
    [Required, StringLength(120, MinimumLength = 1)] public required string Name { get; init; }
    [Required] public required PlanLocation Origin { get; init; }
    public bool CanDrive { get; init; }
    public int PassengerSeats { get; init; }
    public bool CanMeet { get; init; }
    public double MaxDetourMinutes { get; init; } = 30;
    public bool? LocationDisclosureAccepted { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return new ValidationResult("Name is required.", ["name"]);
        if (ExpectedRevision < 1) yield return new ValidationResult("Expected revision must be positive.", ["expectedRevision"]);
        if (PassengerSeats is < 0 or > 8)
            yield return new ValidationResult("Passenger seats must be between 0 and 8.", ["passengerSeats"]);
        if (!CanDrive && PassengerSeats > 0)
            yield return new ValidationResult("Passenger seats can only be offered when canDrive is true.", ["passengerSeats"]);
        if (!double.IsFinite(MaxDetourMinutes) || MaxDetourMinutes is < 0 or > 1440)
            yield return new ValidationResult("Maximum detour must be between 0 and 1440 minutes.", ["maxDetourMinutes"]);
        if (LocationDisclosureAccepted == false)
            yield return new ValidationResult("A previously accepted location-sharing disclosure cannot be withdrawn as part of a profile edit.", ["locationDisclosureAccepted"]);
        foreach (var error in PlanValidation.ValidateLocation(Origin, "origin")) yield return error;
    }
}

public sealed class SelectTripPlanRequest : IValidatableObject
{
    public long ExpectedRevision { get; init; }
    [Required, StringLength(20)] public required string Strategy { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ExpectedRevision < 1) yield return new ValidationResult("Expected revision must be positive.", ["expectedRevision"]);
        if (Strategy is not ("pickup" or "rendezvous")) yield return new ValidationResult("Strategy must be pickup or rendezvous.", ["strategy"]);
    }
}

public sealed record CreateTripResponse(string TripId, long Revision, string OrganizerCapability, DateTimeOffset OrganizerExpiresAt, string InviteCapability, DateTimeOffset InviteExpiresAt);
public sealed record JoinTripResponse(string ParticipantId, string ParticipantCapability, long Revision);
public sealed record InviteTripView(string TripId, string Name, DateTimeOffset ArrivalAt, string TimeZoneId, PlanLocation Destination, DateTimeOffset InviteExpiresAt);
public sealed record TripParticipantView(string Id, string Name, PlanLocation Origin, bool CanDrive, int PassengerSeats, bool CanMeet, double MaxDetourMinutes, bool LocationDisclosureAccepted, DateTimeOffset JoinedAt);
public sealed record OrganizerTripView(string TripId, string Name, DateTimeOffset ArrivalAt, string TimeZoneId, PlanLocation Destination, DateTimeOffset InviteExpiresAt, long Revision, IReadOnlyList<TripParticipantView> Participants, string? SelectedStrategy, GeneratedTripPlanView? CurrentPlan, bool PlanStale);
public sealed record GeneratedTripPlanView(string PlanId, long BasedOnRevision, CarpoolPlanResponse Plan);
public sealed record ParticipantAssignment(string Strategy, string Role, PersonSummary Driver, IReadOnlyList<PersonSummary> Riders, IReadOnlyList<PlanStop> Stops, double TotalDistanceMeters, double VehicleDistanceMeters, double IndependentTravelDistanceMeters, double DurationMinutes, IReadOnlyList<IndependentTravelLeg> IndependentTravelLegs);
public sealed record ParticipantTripView(string TripId, string Name, DateTimeOffset ArrivalAt, string TimeZoneId, PlanLocation Destination, DateTimeOffset InviteExpiresAt, long Revision, TripParticipantView Profile, ParticipantAssignment? Assignment, string? UnassignedReason, bool PlanStale);
public sealed record GenerateTripPlanResponse(string PlanId, long BasedOnRevision, CarpoolPlanResponse Plan);
public sealed record CurrentRevisionResponse(long Revision);

internal static class TripValidation
{
    public static bool TryParseArrival(string? value, out DateTimeOffset arrival)
    {
        arrival = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var hasExplicitOffset = value.EndsWith('Z') ||
            (value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':');
        return hasExplicitOffset && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out arrival);
    }

    public static bool MatchesTimeZone(DateTimeOffset arrival, string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(arrival, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).Offset == arrival.Offset;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
        catch (ArgumentException) { return false; }
    }
}

public sealed class TripApiException(int status, string title, string detail, string? code = null, long? expectedRevision = null, long? currentRevision = null, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(detail)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public string? Code { get; } = code;
    public long? ExpectedRevision { get; } = expectedRevision;
    public long? CurrentRevision { get; } = currentRevision;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}

public sealed record RotateInviteResponse(string InviteCapability, DateTimeOffset InviteExpiresAt);
public sealed record RotateOrganizerResponse(string OrganizerCapability, DateTimeOffset OrganizerExpiresAt);
