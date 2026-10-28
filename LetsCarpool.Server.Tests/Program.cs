using System.ComponentModel.DataAnnotations;
using LetsCarpool.Server.Carpooling;
using LetsCarpool.Server.Trips;

var checks = new (string Name, Action Run)[]
{
    ("capacity and unique coverage", CapacityAndCoverage),
    ("pickup stop order", PickupOrder),
    ("rendezvous independent travel and consent", RendezvousCostAndConsent),
    ("validation errors", ValidationErrors),
    ("no drivers and zero distance", NoDriversAndZeroDistance),
    ("detour rejection", DetourRejection),
    ("participant bound", ParticipantBound),
    ("operation budget", OperationBudget),
    ("driver availability and per-driver detour", DriverAvailabilityAndDetour),
    ("trip persistence and token hashing", TripTests.PersistenceAndTokenHashing),
    ("trip capability isolation and invite rotation", TripTests.CapabilityIsolationAndRotation),
    ("join idempotency and concurrent capacity", TripTests.JoinIdempotencyAndCapacity),
    ("trip plan revision invalidation and private assignment", TripTests.PlanRevisionAndPrivateAssignment),
    ("trip and join request validation", TripTests.TripAndJoinValidation),
    ("revision race during plan generation", TripTests.GenerationRevisionRace)
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
    Equal("destination", group.Stops[^1].Kind);
    Equal(0, group.IndependentTravelLegs.Count);
    True(result.SearchOperations <= result.SearchOperationLimit);
}

static void RendezvousCostAndConsent()
{
    var participants = new[]
    {
        Person("driver", 0, 0, 2),
        Person("rider-a", 0, .05, canMeet: true),
        Person("rider-b", .02, .1, canMeet: true)
    };
    var destination = Location(0, .2);
    var result = Plan(participants, destination);
    var group = result.Alternatives.Single(item => item.Strategy == "rendezvous").Groups.Single();
    True(group.IndependentTravelDistanceMeters > 0);
    Equal(group.Riders.Count, group.IndependentTravelLegs.Count);
    True(group.Riders.Select(rider => rider.Id).OrderBy(id => id, StringComparer.Ordinal)
        .SequenceEqual(group.IndependentTravelLegs.Select(leg => leg.ParticipantId).OrderBy(id => id, StringComparer.Ordinal)));
    True(group.IndependentTravelLegs.All(leg => leg.From.Kind == "origin" && leg.To.Kind == "rendezvous"));
    var meeting = group.Stops[0];
    var participantsById = participants.ToDictionary(person => person.Id, StringComparer.Ordinal);
    foreach (var leg in group.IndependentTravelLegs)
    {
        var rider = participantsById[leg.ParticipantId];
        Equal(leg.ParticipantId, leg.From.ParticipantId);
        Equal(rider.Origin.Label, leg.From.Label);
        Near(rider.Origin.Latitude, leg.From.Latitude);
        Near(rider.Origin.Longitude, leg.From.Longitude);
        Equal(meeting.ParticipantId, leg.To.ParticipantId);
        Equal(meeting.Label, leg.To.Label);
        Near(meeting.Latitude, leg.To.Latitude);
        Near(meeting.Longitude, leg.To.Longitude);
    }
    Near(group.IndependentTravelLegs.Sum(leg => leg.DistanceMeters), group.IndependentTravelDistanceMeters);
    var directDuration = new HaversineDistanceProvider().Estimate(participantsById["driver"].Origin, destination).DurationMinutes;
    Near(directDuration + group.DriverDetourMinutes + group.IndependentTravelLegs.Sum(leg => leg.DurationMinutes), group.DurationMinutes);
    Near(group.VehicleDistanceMeters + group.IndependentTravelDistanceMeters, group.TotalDistanceMeters);
    Equal(2, group.Riders.Count);
    Equal("destination", group.Stops[^1].Kind);
    True(group.IndependentTravelLegs.All(leg => leg.ModeAssumption == "unspecified"));

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

static void OperationBudget()
{
    var planner = new CarpoolPlanner(new HaversineDistanceProvider(), searchOperationLimit: 1);
    var request = new CarpoolPlanRequest
    {
        Destination = Location(0, .1),
        Participants = [Person("driver", 0, 0, seats: 1), Person("rider", 0, .05)]
    };
    try
    {
        planner.Plan(request);
        throw new InvalidOperationException("Expected planner operation limit to be enforced.");
    }
    catch (PlannerSearchLimitException exception)
    {
        Equal(1, exception.OperationLimit);
    }
}

static void DriverAvailabilityAndDetour()
{
    var solo = Plan([new PlanParticipant { Id = "solo", Name = "Solo", Origin = Location(0, 0), CanDrive = true, PassengerSeats = 0 }], Location(0, .1));
    var soloPickup = solo.Alternatives.Single(alternative => alternative.Strategy == "pickup").Groups.Single();
    Equal("solo", soloPickup.Driver.Id);
    Equal(0, soloPickup.Riders.Count);

    var request = new CarpoolPlanRequest
    {
        Destination = Location(0, .2),
        MaxDetourMinutes = 30,
        Participants =
        [
            new PlanParticipant { Id = "strict", Name = "Strict", Origin = Location(0, 0), CanDrive = true, PassengerSeats = 1, MaxDetourMinutes = 0 },
            new PlanParticipant { Id = "flexible", Name = "Flexible", Origin = Location(.1, 0), CanDrive = true, PassengerSeats = 1, MaxDetourMinutes = 30 },
            Person("rider", .1, 0)
        ]
    };
    var pickup = new CarpoolPlanner(new HaversineDistanceProvider()).Plan(request).Alternatives.Single(item => item.Strategy == "pickup");
    Equal(0, pickup.Groups.Single(group => group.Driver.Id == "strict").Riders.Count);
    Equal("rider", pickup.Groups.Single(group => group.Driver.Id == "flexible").Riders.Single().Id);
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
