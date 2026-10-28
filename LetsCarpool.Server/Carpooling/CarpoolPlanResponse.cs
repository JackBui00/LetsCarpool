namespace LetsCarpool.Server.Carpooling;

public sealed record CarpoolPlanResponse(IReadOnlyList<PlanAlternative> Alternatives);
public sealed record PlanAlternative(
    string Strategy,
    IReadOnlyList<PlanGroup> Groups,
    double TotalDistanceMeters,
    double DurationMinutes,
    IReadOnlyList<UnassignedParticipant> Unassigned,
    string DistanceSource,
    string Optimality,
    IReadOnlyList<string> Warnings);
public sealed record PlanGroup(
    PersonSummary Driver,
    IReadOnlyList<PersonSummary> Riders,
    IReadOnlyList<PlanStop> Stops,
    IReadOnlyList<IndependentTravelLeg> IndependentTravelLegs,
    double VehicleDistanceMeters,
    double IndependentTravelDistanceMeters,
    double TotalDistanceMeters,
    double DurationMinutes,
    double DriverDetourMinutes);
public sealed record PersonSummary(string Id, string Name);
public sealed record PlanStop(string ParticipantId, string Label, double Latitude, double Longitude, string Kind);
public sealed record IndependentTravelLeg(string ParticipantId, PlanStop From, PlanStop To, double DistanceMeters, double DurationMinutes);
public sealed record UnassignedParticipant(string ParticipantId, string Reason);

public sealed record DistanceEstimate(double DistanceMeters, double DurationMinutes);
public interface IDistanceProvider
{
    DistanceEstimate Estimate(PlanLocation from, PlanLocation to);
}

public sealed class HaversineDistanceProvider : IDistanceProvider
{
    public const double AssumedSpeedKilometersPerHour = 40;
    private const double EarthRadiusMeters = 6_371_000;

    public DistanceEstimate Estimate(PlanLocation from, PlanLocation to)
    {
        var lat1 = DegreesToRadians(from.Latitude);
        var lat2 = DegreesToRadians(to.Latitude);
        var deltaLat = lat2 - lat1;
        var deltaLon = DegreesToRadians(to.Longitude - from.Longitude);
        var haversine = Math.Pow(Math.Sin(deltaLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLon / 2), 2);
        var distance = 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(Math.Clamp(haversine, 0, 1)));
        return new DistanceEstimate(distance, distance / (AssumedSpeedKilometersPerHour * 1000 / 60));
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180;
}
