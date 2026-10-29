:setvar ExpectedDatabase "REPLACE_WITH_APPROVED_DATABASE_NAME"

IF DB_NAME() <> N'$(ExpectedDatabase)'
BEGIN
    ;THROW 51000, 'Trip-store schema script stopped: current database does not match ExpectedDatabase.', 1;
END;
IF N'$(ExpectedDatabase)' = N'REPLACE_WITH_APPROVED_DATABASE_NAME'
BEGIN
    ;THROW 51000, 'Replace ExpectedDatabase with the explicitly approved empty database before running this script.', 1;
END;
IF SCHEMA_ID(N'LetCarpool') IS NOT NULL
BEGIN
    ;THROW 51000, 'LetCarpool schema already exists. Inspect it and prepare a reviewed migration instead of rerunning initialization.', 1;
END;

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    EXEC(N'CREATE SCHEMA [LetCarpool] AUTHORIZATION [dbo]');

    CREATE TABLE [LetCarpool].[SchemaMigrations]
    (
        [MigrationVersion] int NOT NULL CONSTRAINT [PK_LetCarpool_SchemaMigrations] PRIMARY KEY,
        [Description] nvarchar(200) NOT NULL,
        [AppliedAtUtc] datetime2(7) NOT NULL CONSTRAINT [DF_LetCarpool_SchemaMigrations_AppliedAtUtc] DEFAULT SYSUTCDATETIME()
    );

    CREATE TABLE [LetCarpool].[TripStoreState]
    (
        [StateId] tinyint NOT NULL CONSTRAINT [PK_LetCarpool_TripStoreState] PRIMARY KEY,
        [SqlLayoutVersion] int NOT NULL,
        [PayloadSchemaVersion] int NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL CONSTRAINT [DF_LetCarpool_TripStoreState_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [CK_LetCarpool_TripStoreState_Singleton] CHECK ([StateId] = 1),
        CONSTRAINT [CK_LetCarpool_TripStoreState_LayoutVersion] CHECK ([SqlLayoutVersion] = 1),
        CONSTRAINT [CK_LetCarpool_TripStoreState_PayloadVersion] CHECK ([PayloadSchemaVersion] = 2),
        CONSTRAINT [CK_LetCarpool_TripStoreState_Json] CHECK (ISJSON([Payload]) = 1)
    );

    INSERT INTO [LetCarpool].[SchemaMigrations] ([MigrationVersion], [Description])
    VALUES (1, N'Initial serialized trip store layout; JSON payload schema remains version 2.');

    INSERT INTO [LetCarpool].[TripStoreState] ([StateId], [SqlLayoutVersion], [PayloadSchemaVersion], [Payload])
    VALUES (1, 1, 2, N'{"schemaVersion":2,"trips":[]}');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
