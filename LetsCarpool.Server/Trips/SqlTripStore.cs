using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace LetsCarpool.Server.Trips;

internal enum TripStoreProvider
{
    Json,
    SqlServer
}

internal sealed class TripStoreUnavailableException : Exception
{
    public TripStoreUnavailableException() : base("The configured SQL trip store is unavailable, not initialized, or has an unsupported schema.") { }
}

internal static class TripStoreProviderSelection
{
    public static TripStoreProvider Parse(string? configuredProvider)
    {
        if (string.IsNullOrWhiteSpace(configuredProvider) || string.Equals(configuredProvider.Trim(), "Json", StringComparison.OrdinalIgnoreCase))
            return TripStoreProvider.Json;
        if (string.Equals(configuredProvider.Trim(), "SqlServer", StringComparison.OrdinalIgnoreCase))
            return TripStoreProvider.SqlServer;
        throw new InvalidOperationException("CarpoolStore:Provider must be either Json or SqlServer.");
    }
}

/// <summary>
/// SQL-backed implementation of the existing whole-database callback store contract.
/// All instances serialize mutations through the database singleton row lock.
/// </summary>
internal sealed class SqlTripStore : ITripStore
{
    private const int SqlLayoutVersion = 1;
    private const int PayloadSchemaVersion = 2;
    private const int CommandTimeoutSeconds = 30;
    private const int MaxPayloadBytes = 16 * 1024 * 1024;
    private const string StateSelect = """
        SELECT [SqlLayoutVersion], [PayloadSchemaVersion], [Payload]
             , [RowVersion]
        FROM [LetCarpool].[TripStoreState] WITH (UPDLOCK, HOLDLOCK)
        WHERE [StateId] = 1;
        """;

    private const string ReadSelect = """
        SELECT [SqlLayoutVersion], [PayloadSchemaVersion], [Payload]
             , [RowVersion]
        FROM [LetCarpool].[TripStoreState]
        WHERE [StateId] = 1;
        """;

    private const string UpdateState = """
        UPDATE [LetCarpool].[TripStoreState]
        SET [PayloadSchemaVersion] = @payloadSchemaVersion,
            [Payload] = @payload,
            [UpdatedAtUtc] = SYSUTCDATETIME()
        WHERE [StateId] = 1 AND [SqlLayoutVersion] = @sqlLayoutVersion AND [RowVersion] = @expectedRowVersion;
        """;

    private static readonly JsonSerializerOptions JsonOptions = JsonTripStore.CreateJsonOptions();
    private readonly string connectionString;

    public SqlTripStore(IConfiguration configuration)
        : this(configuration, allowTestOnlyLocalSqlAuthentication: false)
    {
    }

    internal SqlTripStore(IConfiguration configuration, bool allowTestOnlyLocalSqlAuthentication)
    {
        connectionString = ValidateConfiguration(configuration, allowTestOnlyLocalSqlAuthentication);
    }

    internal static string ValidateConfiguration(IConfiguration configuration) => ValidateConfiguration(configuration, false);

    internal static string ValidateConfiguration(IConfiguration configuration, bool allowTestOnlyLocalSqlAuthentication)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("CarpoolStore:Provider is SqlServer but ConnectionStrings:DefaultConnection is missing.");

        try
        {
            var builder = new SqlConnectionStringBuilder(configured);
            if (string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog))
                throw new InvalidOperationException("The SQL trip store requires a server and database in ConnectionStrings:DefaultConnection.");
            if (!builder.Encrypt)
                throw new InvalidOperationException("The SQL trip store requires encrypted connections.");

            var isTestOnlySqlAuthentication = allowTestOnlyLocalSqlAuthentication
                && string.Equals(Environment.GetEnvironmentVariable("LETS_CARPOOL_RUN_SQL_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase)
                && IsDisposableLocalTestTarget(builder)
                && (builder.Authentication is SqlAuthenticationMethod.NotSpecified or SqlAuthenticationMethod.SqlPassword)
                && !string.IsNullOrWhiteSpace(builder.UserID)
                && !string.IsNullOrEmpty(builder.Password);
            if (isTestOnlySqlAuthentication)
                return builder.ConnectionString;

            if (builder.TrustServerCertificate)
                throw new InvalidOperationException("The SQL trip store requires TrustServerCertificate=False.");
            if (!string.IsNullOrEmpty(builder.Password))
                throw new InvalidOperationException("Password-based SQL credentials are not supported for the trip store.");

            var supportedAuth = builder.Authentication is SqlAuthenticationMethod.ActiveDirectoryDefault
                or SqlAuthenticationMethod.ActiveDirectoryManagedIdentity;
            if (!supportedAuth && !builder.IntegratedSecurity)
                throw new InvalidOperationException("Use Active Directory Default locally or Active Directory Managed Identity in Azure for the SQL trip store.");
            if (!string.IsNullOrWhiteSpace(builder.UserID)
                && builder.Authentication != SqlAuthenticationMethod.ActiveDirectoryManagedIdentity)
                throw new InvalidOperationException("A user ID is only supported as the client ID for a user-assigned managed identity.");

            return builder.ConnectionString;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not a valid SQL trip-store connection string.", exception);
        }
    }

    internal static bool IsDisposableLocalTestTarget(SqlConnectionStringBuilder builder)
    {
        var source = builder.DataSource.Trim();
        var loopback = source.Equals(".", StringComparison.OrdinalIgnoreCase)
            || source.Equals("(local)", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(".\\", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase)
            || source.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("localhost,", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("localhost\\", StringComparison.OrdinalIgnoreCase)
            || source.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("127.0.0.1,", StringComparison.OrdinalIgnoreCase)
            || source.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("[::1],", StringComparison.OrdinalIgnoreCase);
        return loopback
            && System.Text.RegularExpressions.Regex.IsMatch(builder.InitialCatalog, "^LetsCarpoolTest_[0-9a-fA-F]{32}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public async Task<T> ReadAsync<T>(Func<TripDatabase, T> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new SqlCommand(ReadSelect, connection) { CommandTimeout = CommandTimeoutSeconds };
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new TripStoreUnavailableException();

            var snapshot = await ReadDatabaseAsync(reader, cancellationToken);
            return read(snapshot.Database);
        }
        catch (SqlException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (SqlException)
        {
            throw new TripStoreUnavailableException();
        }
    }

    public async Task<T> MutateAsync<T>(Func<TripDatabase, T> mutate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        SqlTransaction transaction;
        try
        {
            transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        }
        catch (SqlException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (SqlException)
        {
            throw new TripStoreUnavailableException();
        }
        await using var transactionScope = transaction;

        try
        {
            await using var select = new SqlCommand(StateSelect, connection, transaction) { CommandTimeout = CommandTimeoutSeconds };
            await using var reader = await select.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new TripStoreUnavailableException();

            var snapshot = await ReadDatabaseAsync(reader, cancellationToken);
            await reader.DisposeAsync();

            var database = snapshot.Database;
            var result = mutate(database);
            if (database.SchemaVersion != PayloadSchemaVersion
                || database.Trips.Any(trip => !IsPersistable(trip.Destination)
                    || trip.Participants.Any(person => !IsPersistable(person.Origin))))
                throw new TripStoreUnavailableException();

            var payload = JsonSerializer.Serialize(database, JsonOptions);
            if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
                throw new TripStoreUnavailableException();

            await using var update = new SqlCommand(UpdateState, connection, transaction) { CommandTimeout = CommandTimeoutSeconds };
            update.Parameters.Add("@payloadSchemaVersion", SqlDbType.Int).Value = PayloadSchemaVersion;
            update.Parameters.Add("@sqlLayoutVersion", SqlDbType.Int).Value = SqlLayoutVersion;
            update.Parameters.Add("@expectedRowVersion", SqlDbType.Binary, 8).Value = snapshot.RowVersion;
            update.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = payload;
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new TripStoreUnavailableException();

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRollbackAsync(transaction);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (SqlException)
        {
            await TryRollbackAsync(transaction);
            throw new TripStoreUnavailableException();
        }
        catch
        {
            await TryRollbackAsync(transaction);
            throw;
        }
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch (OperationCanceledException)
        {
            await connection.DisposeAsync();
            throw;
        }
        catch (SqlException) when (cancellationToken.IsCancellationRequested)
        {
            await connection.DisposeAsync();
            throw new OperationCanceledException(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw new TripStoreUnavailableException();
        }
    }

    private static async Task TryRollbackAsync(SqlTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch { }
    }

    private static async Task<(TripDatabase Database, byte[] RowVersion)> ReadDatabaseAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        var sqlLayoutVersion = reader.GetInt32(0);
        var payloadSchemaVersion = reader.GetInt32(1);
        var payload = await reader.GetFieldValueAsync<string>(2, cancellationToken);
        var rowVersion = await reader.GetFieldValueAsync<byte[]>(3, cancellationToken);
        if (rowVersion.Length != 8)
            throw new TripStoreUnavailableException();
        if (sqlLayoutVersion != SqlLayoutVersion || payloadSchemaVersion != PayloadSchemaVersion)
            throw new TripStoreUnavailableException();
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
            throw new TripStoreUnavailableException();

        TripDatabase database;
        try
        {
            database = JsonSerializer.Deserialize<TripDatabase>(payload, JsonOptions)
                ?? throw new InvalidDataException("The SQL trip store payload could not be read.");
        }
        catch (JsonException)
        {
            throw new TripStoreUnavailableException();
        }

        if (database.SchemaVersion != PayloadSchemaVersion
            || database.Trips.Any(trip => !IsPersistable(trip.Destination)
                || trip.Participants.Any(person => !IsPersistable(person.Origin))))
            throw new TripStoreUnavailableException();
        return (database, rowVersion);
    }

    private static bool IsPersistable(TripLocation location) => location.Provenance is not null
        and not TripLocationProvenance.MapboxSearchBoxTemporary;
}
