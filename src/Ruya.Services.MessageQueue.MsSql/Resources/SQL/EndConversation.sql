--! Ruya MessageQueue - Settle a Service Broker delivery by ending its conversation.
--! Parameters:
--!   @p0 (UNIQUEIDENTIFIER) - Conversation to end.
--!   @p1 (BIT) - Debug mode (1 prints the operation without settlement; 0 executes it).
--! Security: Uses a typed conversation handle and no dynamic SQL.
--! Transaction: Runs inside the caller-owned receive transaction.
SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 UNIQUEIDENTIFIER = NEWID();
DECLARE @p1 BIT = 1;
*/

DECLARE @ConversationHandle UNIQUEIDENTIFIER = @p0;
DECLARE @Debug BIT = COALESCE(@p1, 0);

IF @ConversationHandle IS NULL OR @ConversationHandle = '00000000-0000-0000-0000-000000000000'
    THROW 51103, 'ConversationHandle is required.', 1;

IF @Debug = 1
BEGIN
    PRINT N'Ruya MessageQueue would end the supplied Service Broker conversation.';
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.conversation_endpoints AS conversation_endpoint
    WHERE conversation_endpoint.[conversation_handle] = @ConversationHandle
)
    THROW 51116, 'The requested Service Broker conversation does not exist.', 1;

DECLARE @StartedTransaction BIT = 0;
IF @@TRANCOUNT = 0
BEGIN
    BEGIN TRANSACTION;
    SET @StartedTransaction = 1;
END;

BEGIN TRY
    END CONVERSATION @ConversationHandle;

    IF @StartedTransaction = 1
        COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @StartedTransaction = 1 AND @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
