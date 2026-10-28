using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LetsCarpool.Server.Carpooling;
using LetsCarpool.Server.Trips;
using Microsoft.Extensions.Configuration;

internal static class TripTests
{
    public static void PersistenceAndTokenHashing()
    {
        using var fixture = new Fixture();
        var created = fixture.Service.CreateAsync(fixture.CreateRequest(), CancellationToken.None).GetAwaiter().GetResult();
        var storedText = File.ReadAllText(fixture.StorePath);
        True(!storedText.Contains(created.OrganizerCapability, StringComparison.Ordinal));
        True(!storedText.Contains(created.InviteCapability, StringComparison.Ordinal));
        True(storedText.Contains(CapabilityTokens.Hash(created.OrganizerCapability), StringComparison.Ordinal));

        var restartedStore = new JsonTripStore(fixture.StorePath);
        var restarted = fixture.CreateService(restartedStore);
        Equal(created.TripId, restarted.GetOrganizerAsync(created.TripId, created.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult().TripId);
        Equal(fixture.Arrival, restarted.GetInviteAsync(created.TripId, created.InviteCapability, CancellationToken.None).GetAwaiter().GetResult().ArrivalAt);
    }

    public static void CapabilityIsolationAndRotation()
    {
        using var fixture = new Fixture();
        var tripA = fixture.Service.CreateAsync(fixture.CreateRequest("Trip A"), CancellationToken.None).GetAwaiter().GetResult();
        var tripB = fixture.Service.CreateAsync(fixture.CreateRequest("Trip B"), CancellationToken.None).GetAwaiter().GetResult();
        var firstKey = Guid.NewGuid().ToString();
        var joinedA = fixture.Join(tripA, firstKey, "rider-a");

        var duplicate = Throws<TripApiException>(() => fixture.Join(tripA, firstKey, "rider-a-retry"));
        Equal("join_already_processed", duplicate.Code);
        True(duplicate.Detail.Contains("will not create another participant", StringComparison.Ordinal));

        // Idempotency is scoped to both trip and invite capability, not globally by UUID.
        fixture.Join(tripB, firstKey, "rider-b");
        fixture.Clock.Advance(TimeSpan.FromDays(8));
        ThrowsStatus(404, () => fixture.Service.GetInviteAsync(tripA.TripId, tripA.InviteCapability, CancellationToken.None).GetAwaiter().GetResult());
        var rotatedInvite = fixture.Service.RotateInviteAsync(tripA.TripId, tripA.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult();
        fixture.Service.GetInviteAsync(tripA.TripId, rotatedInvite.InviteCapability, CancellationToken.None).GetAwaiter().GetResult();
        fixture.Join(new CreateTripResponse(tripA.TripId, tripA.Revision + 1, tripA.OrganizerCapability, tripA.OrganizerExpiresAt, rotatedInvite.InviteCapability, rotatedInvite.InviteExpiresAt), firstKey, "rider-a-second");

        ThrowsStatus(404, () => fixture.Service.GetInviteAsync(tripA.TripId, tripA.InviteCapability, CancellationToken.None).GetAwaiter().GetResult());
        fixture.Clock.Advance(TimeSpan.FromDays(43));
        ThrowsStatus(404, () => fixture.Service.GetInviteAsync(tripA.TripId, rotatedInvite.InviteCapability, CancellationToken.None).GetAwaiter().GetResult());
        ThrowsStatus(404, () => fixture.Service.GetParticipantAsync(tripA.TripId, joinedA.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult());
        ThrowsStatus(404, () => fixture.Service.GetOrganizerAsync(tripA.TripId, joinedA.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult());
        ThrowsStatus(404, () => fixture.Service.GetParticipantAsync(tripB.TripId, joinedA.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult());
        ThrowsStatus(404, () => fixture.Service.GetInviteAsync(tripA.TripId, tripA.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult());

        fixture.Service.RevokeInviteAsync(tripA.TripId, tripA.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult();
        ThrowsStatus(404, () => fixture.Service.GetInviteAsync(tripA.TripId, rotatedInvite.InviteCapability, CancellationToken.None).GetAwaiter().GetResult());
        var rotatedOwner = fixture.Service.RotateOrganizerAsync(tripA.TripId, tripA.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult();
        ThrowsStatus(404, () => fixture.Service.GetOrganizerAsync(tripA.TripId, tripA.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult());
        fixture.Service.GetOrganizerAsync(tripA.TripId, rotatedOwner.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static void JoinIdempotencyAndCapacity()
    {
        using var fixture = new Fixture();
        var trip = fixture.Service.CreateAsync(fixture.CreateRequest(), CancellationToken.None).GetAwaiter().GetResult();
        var outcomes = Enumerable.Range(0, 9).Select(index => Task.Run(async () =>
        {
            try
            {
                await fixture.Service.JoinAsync(trip.TripId, trip.InviteCapability, Guid.NewGuid().ToString(), fixture.JoinRequest($"person-{index}"), CancellationToken.None);
                return true;
            }
            catch (TripApiException exception) when (exception.Status == 409)
            {
                return false;
            }
        })).ToArray();
        Equal(6, Task.WhenAll(outcomes).GetAwaiter().GetResult().Count(result => result));
        Equal(6, fixture.Service.GetOrganizerAsync(trip.TripId, trip.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult().Participants.Count);
    }

    public static void PlanRevisionAndPrivateAssignment()
    {
        using var fixture = new Fixture();
        var trip = fixture.Service.CreateAsync(fixture.CreateRequest(), CancellationToken.None).GetAwaiter().GetResult();
        var driver = fixture.Service.JoinAsync(trip.TripId, trip.InviteCapability, Guid.NewGuid().ToString(), fixture.JoinRequest("Driver", canDrive: true, seats: 2, longitude: 0), CancellationToken.None).GetAwaiter().GetResult();
        var riderA = fixture.Service.JoinAsync(trip.TripId, trip.InviteCapability, Guid.NewGuid().ToString(), fixture.JoinRequest("Rider A", longitude: .05), CancellationToken.None).GetAwaiter().GetResult();
        var riderB = fixture.Service.JoinAsync(trip.TripId, trip.InviteCapability, Guid.NewGuid().ToString(), fixture.JoinRequest("Rider B", latitude: .02, longitude: .1), CancellationToken.None).GetAwaiter().GetResult();
        var revision = riderB.Revision;
        var generated = fixture.Service.GeneratePlanAsync(trip.TripId, trip.OrganizerCapability, revision, CancellationToken.None).GetAwaiter().GetResult();
        True(generated.Alternatives.Any(alternative => alternative.Strategy == "rendezvous"));
        fixture.Service.SelectPlanAsync(trip.TripId, generated.PlanId, trip.OrganizerCapability, revision, "rendezvous", CancellationToken.None).GetAwaiter().GetResult();

        var guestView = fixture.Service.GetParticipantAsync(trip.TripId, riderA.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult();
        True(guestView.Assignment is not null);
        Equal(riderA.ParticipantId, guestView.Assignment!.OwnIndependentTravelLeg!.ParticipantId);
        True(guestView.Assignment.OwnIndependentTravelLeg.From.Kind == "origin");
        True(guestView.Assignment.OwnIndependentTravelLeg.To.Kind == "rendezvous");

        var update = fixture.Service.UpdateParticipantAsync(trip.TripId, riderA.ParticipantCapability, new UpdateParticipantRequest
        {
            ExpectedRevision = revision,
            Name = "Rider A updated",
            Origin = fixture.Location(0, .055),
            CanMeet = true
        }, CancellationToken.None).GetAwaiter().GetResult();
        Equal(revision + 1, update.Revision);
        var organizerView = fixture.Service.GetOrganizerAsync(trip.TripId, trip.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult();
        True(organizerView.CurrentPlan is null && organizerView.PlanStale);
        ThrowsStatus(409, () => fixture.Service.SelectPlanAsync(trip.TripId, generated.PlanId, trip.OrganizerCapability, revision, "rendezvous", CancellationToken.None).GetAwaiter().GetResult());
        var refreshedGuest = fixture.Service.GetParticipantAsync(trip.TripId, riderA.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult();
        True(refreshedGuest.Assignment is null && refreshedGuest.PlanStale);
        True(driver.ParticipantId != riderB.ParticipantId);

        var current = fixture.Service.GetOrganizerAsync(trip.TripId, trip.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult();
        var regenerated = fixture.Service.GeneratePlanAsync(trip.TripId, trip.OrganizerCapability, current.Revision, CancellationToken.None).GetAwaiter().GetResult();
        fixture.Service.SelectPlanAsync(trip.TripId, regenerated.PlanId, trip.OrganizerCapability, current.Revision, "pickup", CancellationToken.None).GetAwaiter().GetResult();
        var destinationUpdate = fixture.Service.UpdateTripAsync(trip.TripId, trip.OrganizerCapability, new UpdateTripRequest
        {
            ExpectedRevision = current.Revision,
            Destination = fixture.Location(0, .25, "Updated destination")
        }, CancellationToken.None).GetAwaiter().GetResult();
        Equal(current.Revision + 1, destinationUpdate.Revision);
        True(destinationUpdate.CurrentPlan is null && destinationUpdate.PlanStale);
        ThrowsStatus(404, () => fixture.Service.GetOrganizerAsync(trip.TripId, driver.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult());
        Equal(riderB.ParticipantId, fixture.Service.GetParticipantAsync(trip.TripId, riderB.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult().Profile.Id);
    }

    public static void TripAndJoinValidation()
    {
        using var fixture = new Fixture();
        var request = new CreateTripRequest
        {
            Name = "Valid name",
            ArrivalAt = fixture.Arrival.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            TimeZoneId = "UTC",
            Destination = fixture.Location(91, 0)
        };
        var createErrors = new List<ValidationResult>();
        True(!Validator.TryValidateObject(request, new ValidationContext(request), createErrors, validateAllProperties: true));
        True(createErrors.Any(error => error.MemberNames.Contains("destination.latitude")));

        var join = new JoinTripRequest
        {
            Name = "Rider",
            Origin = fixture.Location(0, 0),
            CanDrive = false,
            PassengerSeats = 1,
            MaxDetourMinutes = double.NaN,
            LocationDisclosureAccepted = false
        };
        var joinErrors = new List<ValidationResult>();
        True(!Validator.TryValidateObject(join, new ValidationContext(join), joinErrors, validateAllProperties: true));
        True(joinErrors.Any(error => error.MemberNames.Contains("passengerSeats")));
        True(joinErrors.Any(error => error.MemberNames.Contains("maxDetourMinutes")));
        True(joinErrors.Any(error => error.MemberNames.Contains("locationDisclosureAccepted")));
    }

    public static void ResponseShapeSerialization()
    {
        using var fixture = new Fixture();
        var trip = fixture.Service.CreateAsync(fixture.CreateRequest(), CancellationToken.None).GetAwaiter().GetResult();
        var driver = fixture.Service.JoinAsync(trip.TripId, trip.InviteCapability, Guid.NewGuid().ToString(), fixture.JoinRequest("Driver", canDrive: true, seats: 1, longitude: 0), CancellationToken.None).GetAwaiter().GetResult();
        var rider = fixture.Service.JoinAsync(trip.TripId, trip.InviteCapability, Guid.NewGuid().ToString(), fixture.JoinRequest("Rider", longitude: .05), CancellationToken.None).GetAwaiter().GetResult();
        var generated = fixture.Service.GeneratePlanAsync(trip.TripId, trip.OrganizerCapability, rider.Revision, CancellationToken.None).GetAwaiter().GetResult();
        fixture.Service.SelectPlanAsync(trip.TripId, generated.PlanId, trip.OrganizerCapability, rider.Revision, "rendezvous", CancellationToken.None).GetAwaiter().GetResult();

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var organizerJson = JsonSerializer.Serialize(fixture.Service.GetOrganizerAsync(trip.TripId, trip.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult(), options);
        using var organizer = JsonDocument.Parse(organizerJson);
        var root = organizer.RootElement;
        True(root.TryGetProperty("inviteExpiresAt", out _));
        True(root.TryGetProperty("selectedStrategy", out var selected) && selected.GetString() == "rendezvous");
        True(!root.TryGetProperty("currentPlanId", out _));
        var currentPlan = root.GetProperty("currentPlan");
        True(currentPlan.TryGetProperty("planId", out _));
        Equal(rider.Revision, currentPlan.GetProperty("basedOnRevision").GetInt64());
        True(currentPlan.GetProperty("alternatives").GetArrayLength() > 0);
        Equal(currentPlan.GetProperty("searchOperationLimit").GetInt32(), generated.SearchOperationLimit);

        var guestJson = JsonSerializer.Serialize(fixture.Service.GetParticipantAsync(trip.TripId, rider.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult(), options);
        using var guest = JsonDocument.Parse(guestJson);
        var guestRoot = guest.RootElement;
        True(guestRoot.TryGetProperty("profile", out var profile));
        Equal("Rider", profile.GetProperty("name").GetString());
        True(!guestRoot.TryGetProperty("self", out _));
        var assignment = guestRoot.GetProperty("assignment");
        True(assignment.GetProperty("totalDistanceMeters").GetDouble() > 0);
        True(assignment.GetProperty("durationMinutes").GetDouble() > 0);
        Equal(rider.ParticipantId, assignment.GetProperty("ownIndependentTravelLeg").GetProperty("participantId").GetString());
        True(assignment.GetProperty("independentTravelDistanceMeters").GetDouble() >= 0);
        True(Math.Abs(assignment.GetProperty("vehicleDistanceMeters").GetDouble() + assignment.GetProperty("independentTravelDistanceMeters").GetDouble() - assignment.GetProperty("totalDistanceMeters").GetDouble()) <= 1e-6);
        True(driver.ParticipantId != rider.ParticipantId);
    }

    public static void GenerationRevisionRace()
    {
        using var fixture = new Fixture();
        var blocking = new BlockingDistanceProvider();
        var service = fixture.CreateService(new JsonTripStore(fixture.StorePath), blocking);
        var trip = service.CreateAsync(fixture.CreateRequest(), CancellationToken.None).GetAwaiter().GetResult();
        var driver = service.JoinAsync(trip.TripId, trip.InviteCapability, Guid.NewGuid().ToString(), fixture.JoinRequest("Driver", canDrive: true, longitude: 0), CancellationToken.None).GetAwaiter().GetResult();
        var generationTask = Task.Run(() => service.GeneratePlanAsync(trip.TripId, trip.OrganizerCapability, driver.Revision, CancellationToken.None));
        True(blocking.Started.Task.Wait(TimeSpan.FromSeconds(10)));
        var updated = service.UpdateParticipantAsync(trip.TripId, driver.ParticipantCapability, new UpdateParticipantRequest
        {
            ExpectedRevision = driver.Revision,
            Name = "Updated driver",
            Origin = fixture.Location(0, .01),
            CanDrive = true,
            PassengerSeats = 0
        }, CancellationToken.None).GetAwaiter().GetResult();
        Equal(driver.Revision + 1, updated.Revision);
        blocking.Release.Set();
        try
        {
            generationTask.GetAwaiter().GetResult();
            throw new InvalidOperationException("Expected a revision conflict after concurrent edit.");
        }
        catch (TripApiException exception)
        {
            Equal(409, exception.Status);
            Equal("revision_conflict", exception.Code);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
        private readonly IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        private readonly string root = Path.Combine(Path.GetTempPath(), "letscarpool-trip-tests-" + Guid.NewGuid().ToString("N"));
        private readonly JsonTripStore store;
        private TripService? service;

        public string StorePath => Path.Combine(root, "trips.json");
        public TripService Service => service ??= CreateService(store);
        public ManualTimeProvider Clock => clock;
        public DateTimeOffset Arrival => clock.GetUtcNow().AddDays(20);

        public Fixture()
        {
            Directory.CreateDirectory(root);
            store = new JsonTripStore(StorePath);
        }

        public TripService CreateService(JsonTripStore store, IDistanceProvider? distanceProvider = null) =>
            new(store, new CarpoolPlanner(distanceProvider ?? new HaversineDistanceProvider()), configuration, clock);

        public CreateTripRequest CreateRequest(string name = "Local trip") => new()
        {
            Name = name,
            ArrivalAt = Arrival.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            TimeZoneId = "UTC",
            Destination = Location(0, .2, "Destination")
        };

        public JoinTripResponse Join(CreateTripResponse trip, string idempotencyKey, string name) =>
            Service.JoinAsync(trip.TripId, trip.InviteCapability, idempotencyKey, JoinRequest(name), CancellationToken.None).GetAwaiter().GetResult();

        public JoinTripRequest JoinRequest(string name, bool canDrive = false, int seats = 0, double latitude = 0, double longitude = .05) => new()
        {
            Name = name,
            Origin = Location(latitude, longitude, name + " origin"),
            CanDrive = canDrive,
            PassengerSeats = seats,
            CanMeet = true,
            MaxDetourMinutes = 30,
            LocationDisclosureAccepted = true
        };

        public PlanLocation Location(double latitude, double longitude, string label = "Somewhere") => new() { Label = label, Latitude = latitude, Longitude = longitude };

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset now = initial;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class BlockingDistanceProvider : IDistanceProvider
    {
        private int calls;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);

        public DistanceEstimate Estimate(PlanLocation from, PlanLocation to)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Started.TrySetResult(true);
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test did not release its blocked matrix estimate.");
            }
            return new HaversineDistanceProvider().Estimate(from, to);
        }
    }

    private static void ThrowsStatus(int status, Action action)
    {
        var exception = Throws<TripApiException>(action);
        Equal(status, exception.Status);
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected condition to be true."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}."); }
}
