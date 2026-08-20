--! Ruya MessageQueue - Send a serialized envelope through a resolved Service Broker service.
--! Parameters:
--!   @p0 (SYSNAME) - Resolved Service Broker service identifier.
--!   @p1 (VARBINARY(MAX)) - Exact serialized envelope bytes.
--!   @p2 (UNIQUEIDENTIFIER OUTPUT) - Created conversation handle.
--!   @p3 (BIT) - Debug mode (1 prints the operation without sending; 0 executes it).
--! Security: The stored procedure verifies the service and quotes dynamic identifiers.
--! Transaction: Runs inside the caller-owned publish or retry transaction.
SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 SYSNAME = N'RuyaServicesMessageQueueService_orders';
DECLARE @p1 VARBINARY(MAX) = 0x00;
DECLARE @p2 UNIQUEIDENTIFIER;
DECLARE @p3 BIT = 1;
*/

DECLARE @ServiceName SYSNAME = @p0;
DECLARE @Payload VARBINARY(MAX) = @p1;
DECLARE @ConversationHandle UNIQUEIDENTIFIER;
DECLARE @Debug BIT = COALESCE(@p3, 0);

IF NULLIF(@ServiceName, N'') IS NULL
    THROW 51109, 'ServiceName is required.', 1;
IF @Payload IS NULL
    THROW 51110, 'Payload is required.', 1;

IF @Debug = 1
BEGIN
    PRINT N'Ruya MessageQueue would send one Service Broker message.';
    RETURN;
END;

IF OBJECT_ID(N'dbo.RuyaServicesMessageQueue_SendMessage', N'P') IS NULL
    THROW 51115, 'The Ruya send procedure is not installed.', 1;

DECLARE @StartedTransaction BIT = 0;
IF @@TRANCOUNT = 0
BEGIN
    BEGIN TRANSACTION;
    SET @StartedTransaction = 1;
END;

BEGIN TRY
    EXEC [dbo].[RuyaServicesMessageQueue_SendMessage]
        @ServiceName = @ServiceName,
        @Payload = @Payload,
        @ConversationHandle = @ConversationHandle OUTPUT;

    SET @p2 = @ConversationHandle;

    IF @StartedTransaction = 1
        COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @StartedTransaction = 1 AND @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
