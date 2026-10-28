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

For SQL integration tests use only a disposable local SQL Server database with a unique name matching `LetsCarpoolTest_<32 hex GUID>` and a loopback endpoint (`localhost`, `127.0.0.1`, `::1`, or a local instance). A CI SQL Server container must publish its port on loopback for the test runner. The harness rejects other targets before opening a connection and requires that the database contain only the schema initialized for this test. Never provide the Azure database connection string to the test harness.

The production store accepts passwordless Entra connections only: `Active Directory Default` locally or `Active Directory Managed Identity` in App Service. The test harness has a separate internal-only path for a disposable local SQL Server container that uses SQL username/password authentication. Such credentials may be passed only through `LETS_CARPOOL_TEST_SQL_CONNECTION`; do not put them in command lines, source, CI artifacts, or logs. The test path requires the exact disposable database name and loopback host, and production configuration validation continues to reject SQL passwords.

Run ordinary focused checks with `dotnet run --project LetsCarpool.Server.Tests`. SQL integration is opt-in and reports `SQL_INTEGRATION_RESULT=SKIP` by default. For a real SQL run, initialize the explicitly named empty disposable database with `001_create_trip_store.sql`, then set both `LETS_CARPOOL_RUN_SQL_INTEGRATION=true` and `LETS_CARPOOL_TEST_SQL_CONNECTION` in the test process environment and run the harness. If opt-in is set but the connection is absent or invalid, the process exits nonzero with a sanitized machine-readable failure. A passing run prints `SQL_INTEGRATION_RESULT=PASS; cases=1; assertions=<count>`; this confirms the real SQL scenario ran. The scenario refuses a non-empty trip state, uses two independent store instances to check visibility/concurrent capacity, verifies idempotency and stale-plan invalidation, checks rollback/cancellation and token privacy, and removes only the trips it creates. The SQL layout script is for local disposable test setup here; its Azure use still requires a separate explicit operator approval.
