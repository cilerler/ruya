--! Ruya MessageQueue - Receive one Service Broker delivery from a topic queue.
--! Parameters:
--!   @p0 (SYSNAME) - Resolved queue identifier.
--!   @p1 (INT) - WAITFOR timeout in milliseconds.
--!   @p2 (BIT) - Debug mode (1 prints the operation without receiving; 0 executes it).
--! Security: The stored procedure validates existence and quotes the queue identifier.
--! Transaction: Runs inside the caller-owned receive transaction.
SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 SYSNAME = N'RuyaServicesMessageQueueQueue_orders';
DECLARE @p1 INT = 1000;
DECLARE @p2 BIT = 1;
*/

DECLARE @QueueName SYSNAME = @p0;
DECLARE @ReceiveTimeoutMs INT = @p1;
DECLARE @Debug BIT = COALESCE(@p2, 0);

IF NULLIF(@QueueName, N'') IS NULL
    THROW 51107, 'QueueName is required.', 1;
IF @ReceiveTimeoutMs < 0
    THROW 51108, 'ReceiveTimeoutMs cannot be negative.', 1;

IF @Debug = 1
BEGIN
    PRINT N'Ruya MessageQueue would receive one message from the supplied queue.';
    RETURN;
END;

IF OBJECT_ID(N'dbo.RuyaServicesMessageQueue_ReceiveMessage', N'P') IS NULL
    THROW 51114, 'The Ruya receive procedure is not installed.', 1;

DECLARE @StartedTransaction BIT = 0;
IF @@TRANCOUNT = 0
BEGIN
    BEGIN TRANSACTION;
    SET @StartedTransaction = 1;
END;

BEGIN TRY
    EXEC [dbo].[RuyaServicesMessageQueue_ReceiveMessage]
        @QueueName = @QueueName,
        @ReceiveTimeoutMs = @ReceiveTimeoutMs;

    IF @StartedTransaction = 1
        COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @StartedTransaction = 1 AND @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
