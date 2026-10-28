using System.Security.Cryptography;
using System.Text.Json;

namespace LetsCarpool.Server.Trips;

internal interface ITripStore
{
    Task<T> ReadAsync<T>(Func<TripDatabase, T> read, CancellationToken cancellationToken = default);
    Task<T> MutateAsync<T>(Func<TripDatabase, T> mutate, CancellationToken cancellationToken = default);
}

internal sealed class JsonTripStore : ITripStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;
    private TripDatabase database;

    public JsonTripStore(IWebHostEnvironment environment, IConfiguration configuration)
        : this(ResolvePath(environment, configuration))
    {
    }

    internal JsonTripStore(string path)
    {
        filePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        RestrictDirectoryPermissions();
        if (File.Exists(filePath))
        {
            database = JsonSerializer.Deserialize<TripDatabase>(File.ReadAllBytes(filePath), JsonOptions)
                ?? throw new InvalidDataException("The local trip store could not be read.");
            if (database.SchemaVersion != 1)
                throw new InvalidDataException("The local trip store has an unsupported schema version.");
        }
        else
        {
            database = new TripDatabase();
        }
    }

    private static string ResolvePath(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configuredPath = configuration["CarpoolStore:FilePath"];
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "trips.json")
            : Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(environment.ContentRootPath, configuredPath);
    }

    public async Task<T> ReadAsync<T>(Func<TripDatabase, T> read, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return read(Clone(database));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T> MutateAsync<T>(Func<TripDatabase, T> mutate, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var changed = Clone(database);
            var result = mutate(changed);
            await PersistAsync(changed, cancellationToken);
            database = changed;
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private static TripDatabase Clone(TripDatabase source) =>
        JsonSerializer.Deserialize<TripDatabase>(JsonSerializer.SerializeToUtf8Bytes(source, JsonOptions), JsonOptions)
        ?? throw new InvalidDataException("The local trip store could not be copied.");

    private async Task PersistAsync(TripDatabase changed, CancellationToken cancellationToken)
    {
        var temporaryPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                RestrictFilePermissions(temporaryPath);
                await JsonSerializer.SerializeAsync(stream, changed, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, filePath, overwrite: true);
            RestrictFilePermissions(filePath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void RestrictDirectoryPermissions()
    {
        if (OperatingSystem.IsWindows()) return; // The user-profile/workspace directory's inherited ACL is the Windows boundary.
        File.SetUnixFileMode(Path.GetDirectoryName(filePath)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RestrictFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

internal static class CapabilityTokens
{
    public static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public static bool Matches(string suppliedToken, string storedHash)
    {
        if (suppliedToken.Length is < 40 or > 64 || storedHash.Length != 64) return false;
        byte[] expected;
        try { expected = Convert.FromHexString(storedHash); }
        catch (FormatException) { return false; }
        if (expected.Length != 32) return false;
        byte[] digest;
        try { digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(suppliedToken)); }
        catch (ArgumentException) { return false; }
        return CryptographicOperations.FixedTimeEquals(expected, digest);
    }
}
