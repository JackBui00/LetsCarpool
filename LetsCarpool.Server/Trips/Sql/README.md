# Azure SQL trip-store setup (operator-reviewed; not executed)

The SQL provider is opt-in. Local development keeps the JSON store unless `CarpoolStore:Provider` is explicitly set to `SqlServer`. The provider stores the accepted JSON document schema version 2 in the separately versioned SQL layout. It does not use the legacy `CarpoolDbContext` tables.

## Local configuration

Use the existing signed-in Visual Studio or Azure CLI Entra identity with SqlClient `Active Directory Default`. Set user-secrets (or an untracked local environment variable); do not put a real connection string into tracked appsettings:

```powershell
dotnet user-secrets set "CarpoolStore:Provider" "SqlServer"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=tcp:letscarpoolserver.database.windows.net,1433;Initial Catalog=LetsCarpoolDatabase;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Default;"
```

No password or service-principal secret is used. The current resolved SqlClient package is pinned directly to 5.1.6, which is already in the dependency graph and depends on Azure.Identity for these authentication modes. Do not upgrade the driver as part of this change.

## App Service configuration

Use the app's system-assigned managed identity and this non-secret setting in the App Service configuration:

```text
CarpoolStore__Provider=SqlServer
ConnectionStrings__DefaultConnection=Server=tcp:letscarpoolserver.database.windows.net,1433;Initial Catalog=LetsCarpoolDatabase;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Managed Identity;
```

For a deliberately selected user-assigned identity only, include `User ID=<managed-identity-client-id>;` in the connection string. Do not put a password, client secret, or access token in App Service settings. The managed identity must already have a contained database principal and the runtime role described below; this code does not provision identity or grants.

## Schema and least-privilege scripts

`001_create_trip_store.sql` is a one-time operator script for an approved empty target only. Enable SQLCMD mode and replace its expected database placeholder after independently checking the target. It refuses a database mismatch or a preexisting `LetCarpool` schema. It creates only `LetCarpool.SchemaMigrations` and `LetCarpool.TripStoreState`, with one empty payload `{"schemaVersion":2,"trips":[]}`. It has not been run against Azure.

Provision the Entra database principal through the separately approved identity workflow, then review `002_runtime_permissions.sql`. Set its database and principal placeholders. That script grants only `SELECT` and `UPDATE` on `LetCarpool.TripStoreState` through `LetCarpoolTripRuntime`; it grants no DDL, legacy-table access, delete, or database-owner rights. A separate migration operator runs schema scripts. Neither script is invoked by app startup.

## Safety and operation

Provider selection fails closed: missing/invalid SQL configuration, missing schema/state row, unsupported SQL layout/payload version, invalid JSON, SQL outage, or size limit produces an error; no JSON fallback occurs. Back up any local JSON file before a separately authorized import/cutover. There is no automatic JSON import, create-database, seed, migration, or destructive rollback. The singleton JSON row serializes every mutation across app instances and is suitable only for a small bounded rollout; monitor lock wait, row/document size, and operation latency before expanding.

For SQL integration tests use only a manually provisioned disposable local database with a unique name matching `LetsCarpoolTest_<hex-guid>`, on a loopback/LocalDB SQL instance. The test harness refuses other targets and requires that the database contain only the schema initialized for this test. Never provide the named Azure database connection string to the test harness.

Set `LETS_CARPOOL_TEST_SQL_CONNECTION` only for that local disposable database, using a passwordless integrated/Entra connection and the required encrypted transport. Then run `dotnet run --project LetsCarpool.Server.Tests`. The integration case checks its target host and unique database name before opening a connection, refuses non-empty state, and removes only the trips it creates. Without this variable it reports a skip; this skip is not evidence that SQL transaction behavior passed.
