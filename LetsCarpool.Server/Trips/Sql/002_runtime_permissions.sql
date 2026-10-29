:setvar ExpectedDatabase "REPLACE_WITH_APPROVED_DATABASE_NAME"
:setvar RuntimeDatabasePrincipal "REPLACE_WITH_PROVISIONED_APP_MANAGED_IDENTITY_PRINCIPAL"

IF DB_NAME() <> N'$(ExpectedDatabase)'
BEGIN
    ;THROW 51000, 'Trip-store permission script stopped: current database does not match ExpectedDatabase.', 1;
END;
IF N'$(ExpectedDatabase)' = N'REPLACE_WITH_APPROVED_DATABASE_NAME'
BEGIN
    ;THROW 51000, 'Replace ExpectedDatabase with the explicitly approved database before running this script.', 1;
END;
IF N'$(RuntimeDatabasePrincipal)' = N'REPLACE_WITH_PROVISIONED_APP_MANAGED_IDENTITY_PRINCIPAL'
BEGIN
    ;THROW 51000, 'Provision the intended managed-identity database principal and set RuntimeDatabasePrincipal before running this script.', 1;
END;
IF DATABASE_PRINCIPAL_ID(N'$(RuntimeDatabasePrincipal)') IS NULL
BEGIN
    ;THROW 51000, 'The configured runtime database principal does not exist; create it through the approved Entra operator process first.', 1;
END;
IF SCHEMA_ID(N'LetCarpool') IS NULL OR OBJECT_ID(N'LetCarpool.TripStoreState', N'U') IS NULL
BEGIN
    ;THROW 51000, 'The reviewed trip-store schema has not been initialized.', 1;
END;

IF DATABASE_PRINCIPAL_ID(N'LetCarpoolTripRuntime') IS NULL
    EXEC(N'CREATE ROLE [LetCarpoolTripRuntime] AUTHORIZATION [dbo]');

DECLARE @AddMember nvarchar(max) = N'ALTER ROLE [LetCarpoolTripRuntime] ADD MEMBER ' + QUOTENAME(N'$(RuntimeDatabasePrincipal)');
IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_role_members AS membership
    JOIN sys.database_principals AS rolePrincipal ON rolePrincipal.principal_id = membership.role_principal_id
    JOIN sys.database_principals AS memberPrincipal ON memberPrincipal.principal_id = membership.member_principal_id
    WHERE rolePrincipal.name = N'LetCarpoolTripRuntime'
      AND memberPrincipal.name = N'$(RuntimeDatabasePrincipal)'
)
    EXEC sys.sp_executesql @AddMember;

GRANT SELECT, UPDATE ON OBJECT::[LetCarpool].[TripStoreState] TO [LetCarpoolTripRuntime];
