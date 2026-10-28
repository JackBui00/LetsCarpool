using System.Text.RegularExpressions;
using LetsCarpool.Server.Carpooling;
using LetsCarpool.Server.Trips;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

internal static class SqlTripStoreTests
{
    private const string TestConnectionVariable = "LETS_CARPOOL_TEST_SQL_CONNECTION";

    public static void ProviderSelectionAndConfiguration()
    {
        Equal(TripStoreProvider.Json, TripStoreProviderSelection.Parse(null));
        Equal(TripStoreProvider.Json, TripStoreProviderSelection.Parse("json"));
        Equal(TripStoreProvider.SqlServer, TripStoreProviderSelection.Parse("SqlServer"));
        ThrowsInvalidOperation(() => TripStoreProviderSelection.Parse("fallback"));

        var missing = new ConfigurationBuilder().AddInMemoryCollection().Build();
        ThrowsInvalidOperation(() => SqlTripStore.ValidateConfiguration(missing));

        var valid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=tcp:example.database.windows.net,1433;Initial Catalog=Example;Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Default"
        }).Build();
        True(!string.IsNullOrWhiteSpace(SqlTripStore.ValidateConfiguration(valid)));

        var password = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Initial Catalog=LetsCarpoolTest_0123456789abcdef0123456789abcdef;Encrypt=True;TrustServerCertificate=False;User ID=test;Password=placeholder"
        }).Build();
        ThrowsInvalidOperation(() => SqlTripStore.ValidateConfiguration(password));
    }

    public static bool RunLocalIntegrationIfConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString)) return false;

        try
        {
            RunLocalIntegration(connectionString);
            return true;
        }
        catch (Exception exception)
        {
            // Do not emit provider errors, connection strings, identity details, or database payloads.
            throw new InvalidOperationException($"SQL integration failed with {exception.GetType().Name}; details are suppressed to protect connection and trip data.");
        }
    }

    private static void RunLocalIntegration(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!IsLoopback(builder.DataSource)
            || !Regex.IsMatch(builder.InitialCatalog, "^LetsCarpoolTest_[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant)
            || !string.IsNullOrEmpty(builder.Password))
            throw new InvalidOperationException("The SQL integration target must be a passwordless, loopback database named LetsCarpoolTest_<32 hex GUID>.");

        var tripStoreConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString
        }).Build();
        var serviceConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mapbox:AllowPermanentGeocodingStorage"] = "false"
        }).Build();

        var firstStore = new SqlTripStore(tripStoreConfiguration);
        var secondStore = new SqlTripStore(tripStoreConfiguration);
        var firstService = CreateService(firstStore, serviceConfiguration);
        var secondService = CreateService(secondStore, serviceConfiguration);
        var prefix = "SQL integration " + Guid.NewGuid().ToString("N");
        var createdTripIds = new List<string>();

        try
        {
            var initialTripCount = firstStore.ReadAsync(database => database.Trips.Count).GetAwaiter().GetResult();
            if (initialTripCount != 0)
                throw new InvalidOperationException("The disposable local SQL test database is not empty; refusing to use it.");

            var created = firstService.CreateAsync(CreateRequest(prefix + " main"), CancellationToken.None).GetAwaiter().GetResult();
            createdTripIds.Add(created.TripId);
            var driver = firstService.JoinAsync(created.TripId, created.InviteCapability, Guid.NewGuid().ToString(), JoinRequest("driver", canDrive: true, seats: 1), CancellationToken.None).GetAwaiter().GetResult();
            var riderKey = Guid.NewGuid().ToString();
            var rider = secondService.JoinAsync(created.TripId, created.InviteCapability, riderKey, JoinRequest("rider"), CancellationToken.None).GetAwaiter().GetResult();
            Equal(2, secondService.GetOrganizerAsync(created.TripId, created.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult().Participants.Count);

            var replay = ThrowsTripApiException(() => firstService.JoinAsync(created.TripId, created.InviteCapability, riderKey, JoinRequest("duplicate"), CancellationToken.None).GetAwaiter().GetResult());
            Equal("join_already_processed", replay.Code);

            var plan = firstService.GeneratePlanAsync(created.TripId, created.OrganizerCapability, rider.Revision, CancellationToken.None).GetAwaiter().GetResult();
            firstService.SelectPlanAsync(created.TripId, plan.PlanId, created.OrganizerCapability, plan.BasedOnRevision, "pickup", CancellationToken.None).GetAwaiter().GetResult();
            var guestView = secondService.GetParticipantAsync(created.TripId, rider.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult();
            True(guestView.Assignment is not null);

            var updated = firstService.UpdateParticipantAsync(created.TripId, driver.ParticipantCapability, new UpdateParticipantRequest
            {
                ExpectedRevision = plan.BasedOnRevision,
                Name = "driver updated",
                Origin = Location("updated driver origin", .01),
                CanDrive = true,
                PassengerSeats = 1,
                CanMeet = true,
                MaxDetourMinutes = 30
            }, CancellationToken.None).GetAwaiter().GetResult();
            True(secondService.GetOrganizerAsync(created.TripId, created.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult().PlanStale);
            Equal(updated.Revision, secondService.GetOrganizerAsync(created.TripId, created.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult().Revision);

            var crossRole = ThrowsTripApiException(() => firstService.GetOrganizerAsync(created.TripId, rider.ParticipantCapability, CancellationToken.None).GetAwaiter().GetResult());
            Equal(404, crossRole.Status);

            var secondTrip = firstService.CreateAsync(CreateRequest(prefix + " second"), CancellationToken.None).GetAwaiter().GetResult();
            createdTripIds.Add(secondTrip.TripId);
            var crossTrip = ThrowsTripApiException(() => secondService.GetInviteAsync(secondTrip.TripId, created.InviteCapability, CancellationToken.None).GetAwaiter().GetResult());
            Equal(404, crossTrip.Status);

            var joins = Enumerable.Range(0, 9).Select(index => Task.Run(async () =>
            {
                try
                {
                    await (index % 2 == 0 ? firstService : secondService).JoinAsync(
                        created.TripId, created.InviteCapability, Guid.NewGuid().ToString(), JoinRequest("concurrent-" + index), CancellationToken.None);
                    return true;
                }
                catch (TripApiException exception) when (exception.Status == 409)
                {
                    return false;
                }
            })).ToArray();
            Equal(4, Task.WhenAll(joins).GetAwaiter().GetResult().Count(joined => joined));
            Equal(6, secondService.GetOrganizerAsync(created.TripId, created.OrganizerCapability, CancellationToken.None).GetAwaiter().GetResult().Participants.Count);

            try
            {
                firstStore.MutateAsync<int>(_ => throw new InvalidOperationException("intentional rollback"), CancellationToken.None).GetAwaiter().GetResult();
                throw new InvalidOperationException("Expected failed mutation to roll back.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "intentional rollback") { }
            Equal(2, secondStore.ReadAsync(database => database.Trips.Count).GetAwaiter().GetResult());

            using var sqlConnection = new SqlConnection(connectionString);
            sqlConnection.Open();
            using var payloadCommand = new SqlCommand("SELECT [Payload] FROM [LetCarpool].[TripStoreState] WHERE [StateId] = 1;", sqlConnection);
            var payload = (string?)payloadCommand.ExecuteScalar() ?? throw new InvalidOperationException("SQL trip-store payload is unavailable.");
            True(!payload.Contains(created.OrganizerCapability, StringComparison.Ordinal));
            True(!payload.Contains(created.InviteCapability, StringComparison.Ordinal));
            True(!payload.Contains(driver.ParticipantCapability, StringComparison.Ordinal));
            True(!payload.Contains(rider.ParticipantCapability, StringComparison.Ordinal));
            True(payload.Contains("\"schemaVersion\":2", StringComparison.Ordinal));
            True(payload.Contains("\"provenance\":\"userProvided\"", StringComparison.Ordinal));

            using var lockCancellation = new CancellationTokenSource();
            using var lockConnection = new SqlConnection(connectionString);
            lockConnection.Open();
            using var rowLockTransaction = lockConnection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            using (var rowLockCommand = new SqlCommand("SELECT [StateId] FROM [LetCarpool].[TripStoreState] WITH (UPDLOCK, HOLDLOCK) WHERE [StateId] = 1;", lockConnection, rowLockTransaction))
            using (var rowLockReader = rowLockCommand.ExecuteReader())
                True(rowLockReader.Read());

            var callbackRan = false;
            var blockedMutation = secondStore.MutateAsync(database =>
            {
                callbackRan = true;
                database.Trips.Clear();
                return true;
            }, lockCancellation.Token);
            Thread.Sleep(100);
            lockCancellation.CancelAfter(TimeSpan.FromMilliseconds(250));
            try
            {
                blockedMutation.GetAwaiter().GetResult();
                throw new InvalidOperationException("Expected the blocked SQL mutation to be canceled.");
            }
            catch (OperationCanceledException) { }
            finally
            {
                rowLockTransaction.Rollback();
            }
            True(!callbackRan);
            Equal(2, firstStore.ReadAsync(database => database.Trips.Count).GetAwaiter().GetResult());
        }
        finally
        {
            firstStore.MutateAsync(database => database.Trips.RemoveAll(trip => createdTripIds.Contains(trip.Id)), CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private static bool IsLoopback(string dataSource)
    {
        var source = dataSource.Trim();
        return source.Equals(".", StringComparison.OrdinalIgnoreCase)
            || source.Equals("(local)", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(".\\", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase)
            || source.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("localhost,", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("localhost\\", StringComparison.OrdinalIgnoreCase)
            || source.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("127.0.0.1,", StringComparison.OrdinalIgnoreCase);
    }

    private static TripService CreateService(ITripStore store, IConfiguration configuration) =>
        new(store, new CarpoolPlanner(new HaversineDistanceProvider()), configuration, TimeProvider.System);

    private static CreateTripRequest CreateRequest(string name) => new()
    {
        Name = name,
        ArrivalAt = DateTimeOffset.UtcNow.AddDays(14).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        TimeZoneId = "UTC",
        Destination = Location("test destination", .2)
    };

    private static JoinTripRequest JoinRequest(string name, bool canDrive = false, int seats = 0) => new()
    {
        Name = name,
        Origin = Location(name + " origin", .05),
        CanDrive = canDrive,
        PassengerSeats = seats,
        CanMeet = true,
        MaxDetourMinutes = 30,
        LocationDisclosureAccepted = true
    };

    private static TripLocation Location(string label, double longitude) => new()
    {
        Label = label,
        Latitude = 0,
        Longitude = longitude,
        Provenance = TripLocationProvenance.UserProvided
    };

    private static TripApiException ThrowsTripApiException(Action action)
    {
        try { action(); }
        catch (TripApiException exception) { return exception; }
        throw new InvalidOperationException("Expected a trip API error.");
    }

    private static void ThrowsInvalidOperation(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected invalid trip-store configuration.");
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected condition to be true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }
}
