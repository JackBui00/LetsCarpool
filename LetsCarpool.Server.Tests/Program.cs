using System.ComponentModel.DataAnnotations;
using LetsCarpool.Server.Carpooling;

var checks = new (string Name, Action Run)[]
{
    ("capacity and unique coverage", CapacityAndCoverage),
    ("pickup stop order", PickupOrder),
    ("rendezvous independent travel and consent", RendezvousCostAndConsent),
    ("validation errors", ValidationErrors),
    ("no drivers and zero distance", NoDriversAndZeroDistance),
    ("detour rejection", DetourRejection),
    ("participant bound", ParticipantBound)
};

foreach (var (name, run) in checks)
{
    run();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"Passed {checks.Length} focused planner checks.");

static void CapacityAndCoverage()
{
    var result = Plan(
        [Person("driver", 0, 0, 1), Person("rider-a", 0, .01), Person("rider-b", 0, .02)],
        Location(0, .1));
    var alternative = result.Alternatives.Single(item => item.Strategy == "pickup");
    var assigned = alternative.Groups.SelectMany(group => group.Riders.Select(rider => rider.Id)).Append(alternative.Groups.Single().Driver.Id).ToArray();
    Equal(2, assigned.Length);
    Equal(2, assigned.Distinct(StringComparer.Ordinal).Count());
    True(alternative.Groups.Single().Riders.Count <= 1);
    Equal(1, alternative.Unassigned.Count);
}

static void PickupOrder()
{
    var result = Plan(
        [Person("driver", 0, 0, 2), Person("near", 0, .05), Person("far", 0, .1)],
        Location(0, .2));
    var group = result.Alternatives.Single(item => item.Strategy == "pickup").Groups.Single();
    Equal("near", group.Stops[0].ParticipantId);
    Equal("far", group.Stops[1].ParticipantId);
    Equal("pickup", group.Stops[0].Kind);
}

static void RendezvousCostAndConsent()
{
    var result = Plan(
        [Person("driver", 0, 0, 2), Person("rider-a", 0, .05, canMeet: true), Person("rider-b", .02, .1, canMeet: true)],
        Location(0, .2));
    var group = result.Alternatives.Single(item => item.Strategy == "rendezvous").Groups.Single();
    True(group.IndependentTravelDistanceMeters > 0);
    Near(group.VehicleDistanceMeters + group.IndependentTravelDistanceMeters, group.TotalDistanceMeters);
    Equal(2, group.Riders.Count);

    var noConsent = Plan([Person("driver", 0, 0, 1), Person("rider", 0, .1, canMeet: false)], Location(0, .2));
    Equal("pickup", noConsent.Alternatives[0].Strategy);
    True(noConsent.Alternatives[0].Groups.Single().Riders.Count > noConsent.Alternatives[1].Groups.Single().Riders.Count);
    var alternative = noConsent.Alternatives.Single(item => item.Strategy == "rendezvous");
    Equal(0, alternative.Groups.Single().Riders.Count);
    Equal("rider", alternative.Unassigned.Single().ParticipantId);
}

static void ValidationErrors()
{
    var request = new CarpoolPlanRequest
    {
        Destination = Location(91, 0),
        Participants = [Person("same", 0, 0), Person("same", 0, 0)],
        MaxDetourMinutes = 30
    };
    var context = new ValidationContext(request);
    var errors = new List<ValidationResult>();
    True(!Validator.TryValidateObject(request, context, errors, validateAllProperties: true));
    True(errors.Count >= 2);
    True(errors.Any(error => error.ErrorMessage!.Contains("Latitude", StringComparison.Ordinal)));
    True(errors.Any(error => error.ErrorMessage!.Contains("unique", StringComparison.Ordinal)));
    var invalidDetour = new CarpoolPlanRequest { Destination = Location(0, 0), Participants = [Person("one", 0, 0)], MaxDetourMinutes = -1 };
    var detourErrors = new List<ValidationResult>();
    True(!Validator.TryValidateObject(invalidDetour, new ValidationContext(invalidDetour), detourErrors, true));
}

static void NoDriversAndZeroDistance()
{
    var noDrivers = Plan([Person("one", 0, 0), Person("two", 0, 0)], Location(0, 0));
    True(noDrivers.Alternatives.All(item => item.Groups.Count == 0));
    True(noDrivers.Alternatives.SelectMany(item => item.Unassigned).Any(person => person.Reason.Contains("No participant offered", StringComparison.Ordinal)));
    var zero = new HaversineDistanceProvider().Estimate(Location(0, 0), Location(0, 0));
    Equal(0d, zero.DistanceMeters);
    Equal(0d, zero.DurationMinutes);
}

static void DetourRejection()
{
    var result = Plan(
        [Person("driver", 0, 0, 1), Person("detour", .1, .1)], Location(0, .2), maxDetourMinutes: 0);
    var alternative = result.Alternatives.Single(item => item.Strategy == "pickup");
    Equal(0, alternative.Groups.Single().Riders.Count);
    Equal("detour", alternative.Unassigned.Single().ParticipantId);
}

static void ParticipantBound()
{
    var participants = Enumerable.Range(0, CarpoolPlanner.MaximumParticipants + 1).Select(index => Person($"p{index}", 0, 0)).ToArray();
    var request = new CarpoolPlanRequest { Destination = Location(0, 0), Participants = participants };
    var errors = new List<ValidationResult>();
    True(!Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
    True(errors.Any(error => error.ErrorMessage!.Contains($"At most {CarpoolPlanner.MaximumParticipants}", StringComparison.Ordinal)));
}

static CarpoolPlanResponse Plan(PlanParticipant[] participants, PlanLocation destination, double maxDetourMinutes = 30) =>
    new CarpoolPlanner(new HaversineDistanceProvider()).Plan(new CarpoolPlanRequest
    {
        Destination = destination,
        Participants = participants,
        MaxDetourMinutes = maxDetourMinutes
    });

static PlanParticipant Person(string id, double latitude, double longitude, int seats = 0, bool canMeet = true) => new()
{
    Id = id,
    Name = id,
    Origin = Location(latitude, longitude),
    PassengerSeats = seats,
    CanMeet = canMeet
};

static PlanLocation Location(double latitude, double longitude) => new() { Label = "Somewhere", Latitude = latitude, Longitude = longitude };
static void True(bool value) { if (!value) throw new InvalidOperationException("Expected condition to be true."); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}."); }
static void Near(double expected, double actual) { if (Math.Abs(expected - actual) > 1e-5) throw new InvalidOperationException($"Expected {expected}; got {actual}."); }
