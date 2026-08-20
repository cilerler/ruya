--! Ruya MessageQueue - Idempotent SQL Server Service Broker schema and topology procedures.
--! Parameters:
--!   @p0 (BIT) - Debug mode (1 compiles subsequent batches with NOEXEC; 0 applies them).
--! Security: Dynamic identifiers are resolved from owned topology and protected with QUOTENAME.
--! Required permission: deployment DDL rights; do not grant them to a DML-only runtime identity.

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 BIT = 1;
*/

SET NOCOUNT ON;
DECLARE @Debug BIT = COALESCE(@p0, 0);
IF @Debug = 1
BEGIN
    PRINT N'Ruya MessageQueue schema batches will be compiled without executing.';
    SET NOEXEC ON;
END;

-- Run this script during deployment when the application uses AutoCreateSchema = false.

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS database_info
    WHERE database_info.[name] = DB_NAME()
      AND database_info.[is_broker_enabled] = 1
)
BEGIN
    PRINT 'WARNING: Service Broker is not enabled on this database.';
    PRINT 'Run: ALTER DATABASE [' + DB_NAME() + '] SET ENABLE_BROKER;';
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.service_message_types AS message_type
    WHERE message_type.[name] = N'RuyaServicesMessageQueueMessage'
)
BEGIN
    CREATE MESSAGE TYPE [RuyaServicesMessageQueueMessage] VALIDATION = NONE;
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.service_contracts AS service_contract
    WHERE service_contract.[name] = N'RuyaServicesMessageQueueContract'
)
BEGIN
    CREATE CONTRACT [RuyaServicesMessageQueueContract]
    (
        [RuyaServicesMessageQueueMessage] SENT BY ANY
    );
END
GO

IF OBJECT_ID(N'dbo.RuyaServicesMessageQueueTopology', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RuyaServicesMessageQueueTopology]
    (
        [TopicName] NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT [PK_RuyaServicesMessageQueueTopology] PRIMARY KEY,
        [QueueName] SYSNAME NOT NULL
            CONSTRAINT [UQ_RuyaServicesMessageQueueTopology_QueueName] UNIQUE,
        [ServiceName] SYSNAME NOT NULL
            CONSTRAINT [UQ_RuyaServicesMessageQueueTopology_ServiceName] UNIQUE,
        [CreatedAt] DATETIME2 NOT NULL
            CONSTRAINT [DF_RuyaServicesMessageQueueTopology_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RuyaServicesMessageQueue_CreateTopicService]
    @TopicName NVARCHAR(255),
    @ProposedQueueName SYSNAME,
    @ProposedServiceName SYSNAME,
    @LegacyQueueName SYSNAME = NULL,
    @LegacyServiceName SYSNAME = NULL,
    @ResolvedQueueName SYSNAME OUTPUT,
    @ResolvedServiceName SYSNAME OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NULLIF(@TopicName, N'') IS NULL
        THROW 51000, 'TopicName is required.', 1;
    IF NULLIF(@ProposedQueueName, N'') IS NULL OR NULLIF(@ProposedServiceName, N'') IS NULL
        THROW 51001, 'Proposed queue and service names are required.', 1;

    DECLARE @StartedTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @StartedTransaction = 1;
    END

    BEGIN TRY
        DECLARE @LockResult INT;
        DECLARE @LockResource NVARCHAR(255) =
            N'Ruya.Services.MessageQueue.MsSql.Topology:' + COALESCE(@LegacyQueueName, @ProposedQueueName);
        EXEC @LockResult = sys.sp_getapplock
            @Resource = @LockResource,
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 60000;
        IF @LockResult < 0
            THROW 51002, 'Could not acquire the Service Broker topology lock.', 1;

        SELECT
            @ResolvedQueueName = topology.[QueueName],
            @ResolvedServiceName = topology.[ServiceName]
        FROM [dbo].[RuyaServicesMessageQueueTopology] AS topology
        WHERE topology.[TopicName] = @TopicName;

        IF @ResolvedQueueName IS NULL
        BEGIN
            -- Unsafe legacy topic spellings can collide (for example a.b and a_b, or case-only
            -- variants under a case-insensitive database collation). Never guess ownership of an
            -- unregistered legacy queue. A registered owner proves the collision and permits this
            -- topic to use its proposed collision-safe topology; otherwise an operator must inspect
            -- the queue and seed the mapping explicitly.
            IF @LegacyQueueName IS NOT NULL
               AND @ProposedQueueName <> @LegacyQueueName
               AND EXISTS
               (
                   SELECT 1
                   FROM sys.service_queues AS service_queue
                   WHERE service_queue.[name] = @LegacyQueueName
                     AND service_queue.[schema_id] = SCHEMA_ID(N'dbo')
               )
               AND NOT EXISTS
               (
                   SELECT 1
                   FROM [dbo].[RuyaServicesMessageQueueTopology] AS legacy_owner
                   WHERE legacy_owner.[QueueName] = @LegacyQueueName
               )
            BEGIN
                THROW 51003, 'Ambiguous legacy topology exists. Seed dbo.RuyaServicesMessageQueueTopology after reconciling the queue.', 1;
            END

            SET @ResolvedQueueName = @ProposedQueueName;
            SET @ResolvedServiceName = @ProposedServiceName;

            INSERT INTO [dbo].[RuyaServicesMessageQueueTopology]
                ([TopicName], [QueueName], [ServiceName])
            VALUES
                (@TopicName, @ResolvedQueueName, @ResolvedServiceName);
        END

        DECLARE @QueueObjectId INT;
        SELECT @QueueObjectId = service_queue.[object_id]
        FROM sys.service_queues AS service_queue
        WHERE service_queue.[name] = @ResolvedQueueName
          AND service_queue.[schema_id] = SCHEMA_ID(N'dbo');

        DECLARE @Sql NVARCHAR(MAX);
        IF @QueueObjectId IS NULL
        BEGIN
            SET @Sql = N'CREATE QUEUE [dbo].' + QUOTENAME(@ResolvedQueueName)
                + N' WITH STATUS = ON, RETENTION = OFF, POISON_MESSAGE_HANDLING (STATUS = OFF);';
            EXEC sys.sp_executesql @Sql;

            SELECT @QueueObjectId = service_queue.[object_id]
            FROM sys.service_queues AS service_queue
            WHERE service_queue.[name] = @ResolvedQueueName
              AND service_queue.[schema_id] = SCHEMA_ID(N'dbo');
        END
        ELSE IF EXISTS
        (
            SELECT 1
            FROM sys.service_queues AS service_queue
            WHERE service_queue.[object_id] = @QueueObjectId
              AND service_queue.[is_poison_message_handling_enabled] = 1
        )
        BEGIN
            SET @Sql = N'ALTER QUEUE [dbo].' + QUOTENAME(@ResolvedQueueName)
                + N' WITH POISON_MESSAGE_HANDLING (STATUS = OFF);';
            EXEC sys.sp_executesql @Sql;
        END

        DECLARE @ExistingServiceQueueId INT;
        SELECT @ExistingServiceQueueId = service.[service_queue_id]
        FROM sys.services AS service
        WHERE service.[name] = @ResolvedServiceName;

        IF @ExistingServiceQueueId IS NULL
        BEGIN
            SET @Sql = N'CREATE SERVICE ' + QUOTENAME(@ResolvedServiceName)
                + N' ON QUEUE [dbo].' + QUOTENAME(@ResolvedQueueName)
                + N' ([RuyaServicesMessageQueueContract]);';
            EXEC sys.sp_executesql @Sql;
        END
        ELSE IF @ExistingServiceQueueId <> @QueueObjectId
        BEGIN
            THROW 51004, 'The mapped Service Broker service belongs to a different queue.', 1;
        END

        IF @StartedTransaction = 1
            COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @StartedTransaction = 1 AND XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

-- Repair only Ruya queues in dbo that still use SQL Server automatic poison handling. Preserve
-- operator-controlled STATUS instead of silently re-enabling a stopped queue.
DECLARE @QueueAlterSql NVARCHAR(MAX) = N'';
SELECT @QueueAlterSql = @QueueAlterSql
    + N'ALTER QUEUE [dbo].' + QUOTENAME(service_queue.[name])
    + N' WITH POISON_MESSAGE_HANDLING (STATUS = OFF);'
FROM sys.service_queues AS service_queue
WHERE service_queue.[schema_id] = SCHEMA_ID(N'dbo')
  AND LEFT(service_queue.[name], LEN(N'RuyaServicesMessageQueueQueue')) = N'RuyaServicesMessageQueueQueue'
  AND service_queue.[is_poison_message_handling_enabled] = 1;
IF LEN(@QueueAlterSql) > 0
    EXEC sys.sp_executesql @QueueAlterSql;
GO

IF OBJECT_ID(N'dbo.RuyaServicesMessageQueueDeadLetter', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RuyaServicesMessageQueueDeadLetter]
    (
        [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [MessageId] NVARCHAR(MAX) NOT NULL,
        [TopicName] NVARCHAR(255) NOT NULL,
        [MessagePayload] VARBINARY(MAX) NOT NULL,
        [ErrorMessage] NVARCHAR(MAX) NULL,
        [DeliveryAttempts] INT NOT NULL,
        [OriginalTimestamp] DATETIME2 NOT NULL,
        [DeadLetterTimestamp] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        INDEX [IX_RuyaServicesMessageQueueDeadLetter_TopicName] ([TopicName]),
        INDEX [IX_RuyaServicesMessageQueueDeadLetter_Timestamp] ([DeadLetterTimestamp])
    );
END
GO

IF EXISTS
(
    SELECT 1
    FROM sys.columns AS column_info
    WHERE column_info.[object_id] = OBJECT_ID(N'dbo.RuyaServicesMessageQueueDeadLetter')
      AND column_info.[name] = N'MessageId'
      AND (column_info.[system_type_id] <> TYPE_ID(N'nvarchar') OR column_info.[max_length] <> -1)
)
BEGIN
    ALTER TABLE [dbo].[RuyaServicesMessageQueueDeadLetter]
        ALTER COLUMN [MessageId] NVARCHAR(MAX) NOT NULL;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RuyaServicesMessageQueue_SendMessage]
    @ServiceName SYSNAME,
    @Payload VARBINARY(MAX),
    @ConversationHandle UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.services AS service
        WHERE service.[name] = @ServiceName
    )
        THROW 51005, 'The requested Service Broker service does not exist.', 1;

    DECLARE @Sql NVARCHAR(MAX) =
        N'BEGIN DIALOG CONVERSATION @ConversationHandle '
        + N'FROM SERVICE ' + QUOTENAME(@ServiceName) + N' '
        + N'TO SERVICE N''' + REPLACE(@ServiceName, N'''', N'''''') + N''' '
        + N'ON CONTRACT [RuyaServicesMessageQueueContract] WITH ENCRYPTION = OFF; '
        + N'SEND ON CONVERSATION @ConversationHandle '
        + N'MESSAGE TYPE [RuyaServicesMessageQueueMessage] (@Payload); '
        + N'END CONVERSATION @ConversationHandle;';

    EXEC sys.sp_executesql
        @Sql,
        N'@Payload VARBINARY(MAX), @ConversationHandle UNIQUEIDENTIFIER OUTPUT',
        @Payload = @Payload,
        @ConversationHandle = @ConversationHandle OUTPUT;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[RuyaServicesMessageQueue_ReceiveMessage]
    @QueueName SYSNAME,
    @ReceiveTimeoutMs INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.service_queues AS service_queue
        WHERE service_queue.[name] = @QueueName
          AND service_queue.[schema_id] = SCHEMA_ID(N'dbo')
    )
        THROW 51006, 'The requested Service Broker queue does not exist.', 1;

    DECLARE @Sql NVARCHAR(MAX) =
        N'WAITFOR (RECEIVE TOP (1) '
        + N'[conversation_handle], [message_type_name], [message_body] '
        + N'FROM [dbo].' + QUOTENAME(@QueueName) + N'), TIMEOUT '
        + CONVERT(NVARCHAR(12), @ReceiveTimeoutMs) + N';';

    EXEC sys.sp_executesql @Sql;
END
GO

IF EXISTS
(
    SELECT 1
    FROM sys.columns AS column_info
    WHERE column_info.[object_id] = OBJECT_ID(N'dbo.RuyaServicesMessageQueueDeadLetter')
      AND column_info.[name] = N'MessagePayload'
      AND (column_info.[system_type_id] <> TYPE_ID(N'varbinary') OR column_info.[max_length] <> -1)
)
BEGIN
    ALTER TABLE [dbo].[RuyaServicesMessageQueueDeadLetter]
        ALTER COLUMN [MessagePayload] VARBINARY(MAX) NOT NULL;
END
GO

-- SET NOEXEC OFF is honored after debug compilation and restores the session for subsequent work.
SET NOEXEC OFF;
GO
