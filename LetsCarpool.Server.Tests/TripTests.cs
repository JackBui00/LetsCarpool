using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LetsCarpool.Server.Carpooling;
using LetsCarpool.Server.Trips;
using LetsCarpool.Server.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;

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
        var rotatedOwnerView = fixture.Service.GetOrganizerAsync(tripA.TripId, rotatedOwner.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult();
        Equal(rotatedOwner.OrganizerExpiresAt, rotatedOwnerView.OrganizerExpiresAt);
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
        True(generated.Plan.Alternatives.Any(alternative => alternative.Strategy == "rendezvous"));
        fixture.Service.SelectPlanAsync(trip.TripId, generated.PlanId, trip.OrganizerCapability, revision, "rendezvous", CancellationToken.None).GetAwaiter().GetResult();

        var guestView = fixture.Service.GetParticipantAsync(trip.TripId, riderA.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult();
        True(guestView.Assignment is not null);
        Equal(riderA.ParticipantId, guestView.Assignment!.IndependentTravelLegs.Single().ParticipantId);
        True(guestView.Assignment.IndependentTravelLegs.Single().From.Kind == "origin");
        True(guestView.Assignment.IndependentTravelLegs.Single().To.Kind == "rendezvous");

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
        var inviteJson = JsonSerializer.Serialize(fixture.Service.GetInviteAsync(trip.TripId, trip.InviteCapability, CancellationToken.None).GetAwaiter().GetResult(), options);
        using var invite = JsonDocument.Parse(inviteJson);
        True(!invite.RootElement.TryGetProperty("organizerExpiresAt", out _));

        var organizerJson = JsonSerializer.Serialize(fixture.Service.GetOrganizerAsync(trip.TripId, trip.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult(), options);
        using var organizer = JsonDocument.Parse(organizerJson);
        var root = organizer.RootElement;
        True(root.TryGetProperty("inviteExpiresAt", out _));
        Equal(trip.OrganizerExpiresAt, root.GetProperty("organizerExpiresAt").GetDateTimeOffset());
        True(root.TryGetProperty("selectedStrategy", out var selected) && selected.GetString() == "rendezvous");
        True(!root.TryGetProperty("currentPlanId", out _));
        var currentPlan = root.GetProperty("currentPlan");
        True(currentPlan.TryGetProperty("planId", out _));
        Equal(rider.Revision, currentPlan.GetProperty("basedOnRevision").GetInt64());
        True(currentPlan.GetProperty("plan").GetProperty("alternatives").GetArrayLength() > 0);
        Equal(currentPlan.GetProperty("plan").GetProperty("searchOperationLimit").GetInt32(), generated.Plan.SearchOperationLimit);

        var guestJson = JsonSerializer.Serialize(fixture.Service.GetParticipantAsync(trip.TripId, rider.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult(), options);
        using var guest = JsonDocument.Parse(guestJson);
        var guestRoot = guest.RootElement;
        True(guestRoot.TryGetProperty("inviteExpiresAt", out _));
        True(!guestRoot.TryGetProperty("organizerExpiresAt", out _));
        True(guestRoot.TryGetProperty("profile", out var profile));
        Equal("Rider", profile.GetProperty("name").GetString());
        True(profile.GetProperty("locationDisclosureAccepted").GetBoolean());
        True(!guestRoot.TryGetProperty("self", out _));
        var assignment = guestRoot.GetProperty("assignment");
        True(assignment.GetProperty("totalDistanceMeters").GetDouble() > 0);
        True(assignment.GetProperty("durationMinutes").GetDouble() > 0);
        Equal(rider.ParticipantId, assignment.GetProperty("independentTravelLegs")[0].GetProperty("participantId").GetString());
        True(assignment.GetProperty("independentTravelDistanceMeters").GetDouble() >= 0);
        True(Math.Abs(assignment.GetProperty("vehicleDistanceMeters").GetDouble() + assignment.GetProperty("independentTravelDistanceMeters").GetDouble() - assignment.GetProperty("totalDistanceMeters").GetDouble()) <= 1e-6);
        True(driver.ParticipantId != rider.ParticipantId);
    }

    public static void LocationProvenanceValidationAndPersistence()
    {
        using var fixture = new Fixture();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        TripJson.Configure(options);

        var missingProvenance = new TripLocation { Label = "Destination", Latitude = 0, Longitude = 0 };
        True(missingProvenance.Validate("destination").Any(error => error.MemberNames.Contains("destination.provenance")));
        var missingRequest = fixture.CreateRequest(destination: missingProvenance);
        var missingErrors = new List<ValidationResult>();
        True(!Validator.TryValidateObject(missingRequest, new ValidationContext(missingRequest), missingErrors, validateAllProperties: true));
        True(missingErrors.Any(error => error.MemberNames.Contains("destination.provenance")));
        Throws<JsonException>(() => JsonSerializer.Deserialize<TripLocation>("""{"label":"X","latitude":0,"longitude":0,"provenance":"unknown"}""", options));

        var temporary = fixture.CreateRequest(destination: fixture.Location(0, .2, "Temporary", TripLocationProvenance.MapboxSearchBoxTemporary));
        var temporaryError = Throws<TripApiException>(() => fixture.Service.CreateAsync(temporary, CancellationToken.None).GetAwaiter().GetResult());
        Equal("validation_error", temporaryError.Code);
        True(temporaryError.Errors!.ContainsKey("destination.provenance"));

        var permanent = fixture.CreateRequest(destination: fixture.Location(0, .2, "Permanent", TripLocationProvenance.MapboxGeocodingPermanent));
        var permanentError = Throws<TripApiException>(() => fixture.Service.CreateAsync(permanent, CancellationToken.None).GetAwaiter().GetResult());
        True(permanentError.Detail.Contains("disabled", StringComparison.Ordinal));

        var trip = fixture.Service.CreateAsync(fixture.CreateRequest(), CancellationToken.None).GetAwaiter().GetResult();
        var serialized = JsonSerializer.Serialize(fixture.Service.GetOrganizerAsync(trip.TripId, trip.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult(), options);
        using (var organizer = JsonDocument.Parse(serialized))
            Equal("userProvided", organizer.RootElement.GetProperty("destination").GetProperty("provenance").GetString());

        var invalidJoin = Throws<TripApiException>(() => fixture.Service.JoinAsync(trip.TripId, trip.InviteCapability, Guid.NewGuid().ToString(), fixture.JoinRequest("Temporary", origin: fixture.Location(0, .05, "Temporary", TripLocationProvenance.MapboxSearchBoxTemporary)), CancellationToken.None).GetAwaiter().GetResult());
        True(invalidJoin.Errors!.ContainsKey("origin.provenance"));
        Equal(0, fixture.Service.GetOrganizerAsync(trip.TripId, trip.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult().Participants.Count);
        var join = fixture.Service.JoinAsync(trip.TripId, trip.InviteCapability, Guid.NewGuid().ToString(), fixture.JoinRequest("Rider"), CancellationToken.None).GetAwaiter().GetResult();
        var invalidEdit = Throws<TripApiException>(() => fixture.Service.UpdateParticipantAsync(trip.TripId, join.ParticipantCapability, new UpdateParticipantRequest
        {
            ExpectedRevision = join.Revision,
            Name = "Rider invalid edit",
            Origin = fixture.Location(0, .06, "Temporary edit", TripLocationProvenance.MapboxSearchBoxTemporary),
            CanMeet = true
        }, CancellationToken.None).GetAwaiter().GetResult());
        True(invalidEdit.Errors!.ContainsKey("origin.provenance"));
        Equal(join.Revision, fixture.Service.GetParticipantAsync(trip.TripId, join.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult().Revision);
        fixture.Service.UpdateParticipantAsync(trip.TripId, join.ParticipantCapability, new UpdateParticipantRequest
        {
            ExpectedRevision = join.Revision,
            Name = "Rider edited",
            Origin = fixture.Location(0, .05, "Rider origin", TripLocationProvenance.UserProvided),
            CanMeet = true
        }, CancellationToken.None).GetAwaiter().GetResult();
        var guest = fixture.Service.GetParticipantAsync(trip.TripId, join.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult();
        Equal(TripLocationProvenance.UserProvided, guest.Profile.Origin.Provenance);

        var updatedTrip = fixture.Service.UpdateTripAsync(trip.TripId, trip.OrganizerCapability, new UpdateTripRequest
        {
            ExpectedRevision = guest.Revision,
            Name = "Renamed"
        }, CancellationToken.None).GetAwaiter().GetResult();
        Equal(TripLocationProvenance.UserProvided, updatedTrip.Destination.Provenance);
        var storedText = File.ReadAllText(fixture.StorePath);
        True(storedText.Contains("\"provenance\":\"userProvided\"", StringComparison.Ordinal));

        using var eligibleFixture = new Fixture(allowPermanentStorage: true);
        var eligibleService = eligibleFixture.CreateService(new JsonTripStore(eligibleFixture.StorePath));
        var permitted = eligibleService.CreateAsync(permanent, CancellationToken.None).GetAwaiter().GetResult();
        Equal(TripLocationProvenance.MapboxGeocodingPermanent,
            eligibleService.GetOrganizerAsync(permitted.TripId, permitted.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult().Destination.Provenance);

        var controller = new TripsController(fixture.Service);
        var action = controller.Create(fixture.CreateRequest(), CancellationToken.None).GetAwaiter().GetResult();
        True(action.Result is CreatedResult);
    }

    public static void LegacyStoreFailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "letscarpool-legacy-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "trips.json");
        const string legacy = "{\"schemaVersion\":1,\"trips\":[]}";
        try
        {
            File.WriteAllText(path, legacy);
            Throws<InvalidDataException>(() => new JsonTripStore(path));
            Equal(legacy, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    public static void JsonStoreDirectoryPermissions()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = Directory.CreateTempSubdirectory("letscarpool-permissions-");
        FileSystemInfo? link = null;
        FileSystemInfo? fileLink = null;
        var sharedRoot = Directory.CreateTempSubdirectory("letscarpool-shared-permissions-");
        var publicRoot = Directory.CreateTempSubdirectory("letscarpool-public-permissions-");
        try
        {
            var originalMode = File.GetUnixFileMode(root.FullName);
            var existingDirectoryStore = new JsonTripStore(Path.Combine(root.FullName, "trips.json"));
            Equal(originalMode, File.GetUnixFileMode(root.FullName));
            existingDirectoryStore.MutateAsync(database => database.Trips.Count, CancellationToken.None).GetAwaiter().GetResult();
            Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(root.FullName, "trips.json")));

            var privateDirectory = Path.Combine(root.FullName, "new-store");
            var privateDirectoryStore = new JsonTripStore(Path.Combine(privateDirectory, "trips.json"));
            Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(privateDirectory));
            Equal(originalMode, File.GetUnixFileMode(root.FullName));
            privateDirectoryStore.MutateAsync(database => database.Trips.Count, CancellationToken.None).GetAwaiter().GetResult();
            Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(privateDirectory, "trips.json")));

            const string schemaTwo = "{\"schemaVersion\":2,\"trips\":[]}";
            var exposedFilePath = Path.Combine(root.FullName, "exposed-trips.json");
            File.WriteAllText(exposedFilePath, schemaTwo);
            var exposedFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
            File.SetUnixFileMode(exposedFilePath, exposedFileMode);
            Throws<InvalidOperationException>(() => new JsonTripStore(exposedFilePath));
            Equal(exposedFileMode, File.GetUnixFileMode(exposedFilePath));
            Equal(schemaTwo, File.ReadAllText(exposedFilePath));

            var fileLinkTarget = Path.Combine(root.FullName, "trusted-target.json");
            File.WriteAllText(fileLinkTarget, schemaTwo);
            var fileLinkTargetMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(fileLinkTarget, fileLinkTargetMode);
            fileLink = File.CreateSymbolicLink(Path.Combine(root.FullName, "store-file-link.json"), fileLinkTarget);
            Throws<InvalidOperationException>(() => new JsonTripStore(fileLink.FullName));
            Equal(fileLinkTargetMode, File.GetUnixFileMode(fileLinkTarget));
            Equal(schemaTwo, File.ReadAllText(fileLinkTarget));
            True(new FileInfo(fileLink.FullName).LinkTarget is not null);

            var sharedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(sharedRoot.FullName, sharedMode);
            Throws<InvalidOperationException>(() => new JsonTripStore(Path.Combine(sharedRoot.FullName, "trips.json")));
            Equal(sharedMode, File.GetUnixFileMode(sharedRoot.FullName));
            var nestedStoreDirectory = Path.Combine(sharedRoot.FullName, "new-store");
            Throws<InvalidOperationException>(() => new JsonTripStore(Path.Combine(nestedStoreDirectory, "trips.json")));
            True(!Directory.Exists(nestedStoreDirectory));
            Equal(sharedMode, File.GetUnixFileMode(sharedRoot.FullName));

            const string attackerControlledLegacy = "{\"schemaVersion\":1,\"trips\":[]}";
            var attackerControlledPath = Path.Combine(publicRoot.FullName, "trips.json");
            File.WriteAllText(attackerControlledPath, attackerControlledLegacy);
            File.SetUnixFileMode(attackerControlledPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var publicMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(publicRoot.FullName, publicMode);
            Throws<InvalidOperationException>(() => new JsonTripStore(attackerControlledPath));
            Equal(publicMode, File.GetUnixFileMode(publicRoot.FullName));
            Equal(attackerControlledLegacy, File.ReadAllText(attackerControlledPath));
            Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(attackerControlledPath));

            var privateDirectoryMode = File.GetUnixFileMode(privateDirectory);
            link = Directory.CreateSymbolicLink(Path.Combine(root.FullName, "store-link"), privateDirectory);
            Throws<InvalidOperationException>(() => new JsonTripStore(Path.Combine(link.FullName, "trips.json")));
            Equal(privateDirectoryMode, File.GetUnixFileMode(privateDirectory));
            True(new DirectoryInfo(link.FullName).LinkTarget is not null);
        }
        finally
        {
            if (link is not null) Directory.Delete(link.FullName);
            if (fileLink is not null) File.Delete(fileLink.FullName);
            Directory.Delete(root.FullName, recursive: true);
            Directory.Delete(sharedRoot.FullName, recursive: true);
            Directory.Delete(publicRoot.FullName, recursive: true);
        }
    }

    public static void PublicApiHttpSecurityAndLimits()
    {
        var serverAssembly = Path.Combine(AppContext.BaseDirectory, "LetsCarpool.Server.dll");
        True(File.Exists(serverAssembly));

        var root = Directory.CreateTempSubdirectory("letscarpool-http-security-");
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(serverAssembly);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        startInfo.Environment["ASPNETCORE_HTTPS_PORT"] = string.Empty;
        startInfo.Environment["HTTPS_PORT"] = string.Empty;
        startInfo.Environment["CarpoolStore__Provider"] = "Json";
        startInfo.Environment["CarpoolStore__FilePath"] = Path.Combine(root.FullName, "store", "trips.json");
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = "Server=127.0.0.1;Database=LetCarpoolNeverConnect;Connect Timeout=1;TrustServerCertificate=True";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The local API test host could not be started.");
        var childExceptionTypes = new List<string>();
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not { } line) return;
            var prefix = line.StartsWith("Unhandled exception. ", StringComparison.Ordinal)
                ? "Unhandled exception. "
                : line.StartsWith(" ---> ", StringComparison.Ordinal) ? " ---> " : null;
            if (prefix is null || childExceptionTypes.Count >= 5) return;
            var exceptionName = line[prefix.Length..];
            var end = exceptionName.IndexOfAny([':', ' ', '\t']);
            childExceptionTypes.Add(end < 0 ? exceptionName : exceptionName[..end]);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(3) };
            var ready = false;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready && DateTime.UtcNow < deadline && !process.HasExited)
            {
                try
                {
                    using var response = client.GetAsync("/api/readiness-probe").GetAwaiter().GetResult();
                    ready = true;
                }
                catch (HttpRequestException)
                {
                    Thread.Sleep(100);
                }
            }
            Console.WriteLine($"HTTP_SECURITY_STAGE=host-ready; ready={ready}; exited={process.HasExited}; exitcode={(process.HasExited ? process.ExitCode : 0)}; exceptionTypes={string.Join(">", childExceptionTypes)}");
            True(ready);

            var sentinel = "private-user-test-sentinel";
            var userPayload = $"{{\"userId\":1,\"email\":\"{sentinel}@example.invalid\",\"passwordHash\":\"{sentinel}\",\"address\":\"{sentinel}\"}}";
            var userRequests = new[]
            {
                new HttpRequestMessage(HttpMethod.Get, "/api/User"),
                new HttpRequestMessage(HttpMethod.Get, "/api/User/1"),
                new HttpRequestMessage(HttpMethod.Post, "/api/User") { Content = JsonContent(userPayload) },
                new HttpRequestMessage(HttpMethod.Put, "/api/User/1") { Content = JsonContent(userPayload) },
                new HttpRequestMessage(HttpMethod.Delete, "/api/User/1")
            };
            foreach (var request in userRequests)
            {
                using (request)
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    Equal(HttpStatusCode.NotFound, response.StatusCode);
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    True(!body.Contains("passwordHash", StringComparison.OrdinalIgnoreCase));
                    True(!body.Contains("email", StringComparison.OrdinalIgnoreCase));
                    True(!body.Contains("address", StringComparison.OrdinalIgnoreCase));
                    True(!body.Contains(sentinel, StringComparison.Ordinal));
                }
            }
            Console.WriteLine("HTTP_SECURITY_STAGE=user-routes-404; passed=5");

            using (var response = client.GetAsync("/api/v1/trips/missing-trip/invite").GetAwaiter().GetResult())
            {
                Console.WriteLine($"HTTP_SECURITY_STAGE=trip-capability-route; status={(int)response.StatusCode}");
                Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            const string planPayload = "{\"destination\":{\"label\":\"Event\",\"latitude\":37.77,\"longitude\":-122.42},\"participants\":[{\"id\":\"rider\",\"name\":\"Rider\",\"origin\":{\"label\":\"Home\",\"latitude\":37.76,\"longitude\":-122.43},\"passengerSeats\":0,\"canMeet\":true}]}";
            using (var response = client.PostAsync("/api/v1/carpool/plan", JsonContent(planPayload)).GetAwaiter().GetResult())
            {
                Console.WriteLine($"HTTP_SECURITY_STAGE=planner-valid; status={(int)response.StatusCode}");
                Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using (var response = client.PostAsync("/api/v1/carpool/plan", JsonContent(planPayload + new string(' ', 33 * 1024))).GetAwaiter().GetResult())
            {
                Console.WriteLine($"HTTP_SECURITY_STAGE=planner-oversize; status={(int)response.StatusCode}");
                Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            }

            var rateLimited = false;
            var acceptedPlans = 0;
            for (var index = 0; index < 12 && !rateLimited; index++)
            {
                using var response = client.PostAsync("/api/v1/carpool/plan", JsonContent(planPayload)).GetAwaiter().GetResult();
                if (response.StatusCode == HttpStatusCode.OK)
                    acceptedPlans++;
                else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    rateLimited = true;
                else
                    throw new InvalidOperationException("The planner HTTP rate-limit check returned an unexpected status.");
            }
            Console.WriteLine($"HTTP_SECURITY_STAGE=planner-rate-limit; accepted={acceptedPlans}; rejected={rateLimited}");
            True(acceptedPlans > 0 && acceptedPlans <= 10);
            True(rateLimited);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            Directory.Delete(root.FullName, recursive: true);
        }

        static StringContent JsonContent(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
    }

    public static void TemporaryStoreLocationsFailClosed()
    {
        using var fixture = new Fixture();
        fixture.Service.CreateAsync(fixture.CreateRequest(), CancellationToken.None).GetAwaiter().GetResult();
        var root = Path.Combine(Path.GetTempPath(), "letscarpool-temporary-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "trips.json");
        var temporary = File.ReadAllText(fixture.StorePath).Replace("\"provenance\":\"userProvided\"", "\"provenance\":\"mapboxSearchBoxTemporary\"", StringComparison.Ordinal);
        try
        {
            File.WriteAllText(path, temporary);
            Throws<InvalidDataException>(() => new JsonTripStore(path));
            Equal(temporary, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
        private readonly bool allowPermanentStorage;
        private readonly string root = Path.Combine(Path.GetTempPath(), "letscarpool-trip-tests-" + Guid.NewGuid().ToString("N"));
        private readonly JsonTripStore store;
        private TripService? service;

        public string StorePath => Path.Combine(root, "trips.json");
        public TripService Service => service ??= CreateService(store);
        public ManualTimeProvider Clock => clock;
        public DateTimeOffset Arrival => clock.GetUtcNow().AddDays(20);

        public Fixture(bool allowPermanentStorage = false)
        {
            this.allowPermanentStorage = allowPermanentStorage;
            Directory.CreateDirectory(root);
            store = new JsonTripStore(StorePath);
        }

        public TripService CreateService(JsonTripStore store, IDistanceProvider? distanceProvider = null) =>
            new(store, new CarpoolPlanner(distanceProvider ?? new HaversineDistanceProvider()),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Mapbox:AllowPermanentGeocodingStorage"] = allowPermanentStorage ? "true" : "false"
                }).Build(), clock);

        public CreateTripRequest CreateRequest(string name = "Local trip", TripLocation? destination = null) => new()
        {
            Name = name,
            ArrivalAt = Arrival.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            TimeZoneId = "UTC",
            Destination = destination ?? Location(0, .2, "Destination")
        };

        public JoinTripResponse Join(CreateTripResponse trip, string idempotencyKey, string name) =>
            Service.JoinAsync(trip.TripId, trip.InviteCapability, idempotencyKey, JoinRequest(name), CancellationToken.None).GetAwaiter().GetResult();

        public JoinTripRequest JoinRequest(string name, bool canDrive = false, int seats = 0, double latitude = 0, double longitude = .05, TripLocation? origin = null) => new()
        {
            Name = name,
            Origin = origin ?? Location(latitude, longitude, name + " origin"),
            CanDrive = canDrive,
            PassengerSeats = seats,
            CanMeet = true,
            MaxDetourMinutes = 30,
            LocationDisclosureAccepted = true
        };

        public TripLocation Location(double latitude, double longitude, string label = "Somewhere", TripLocationProvenance provenance = TripLocationProvenance.UserProvided) => new()
        {
            Label = label, Latitude = latitude, Longitude = longitude, Provenance = provenance
        };

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
