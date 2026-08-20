--! Ruya MessageQueue tests - Disable a test-owned queue while enabling SQL poison handling.
--! Parameters:
--!   @p0 (SYSNAME) - Test-owned queue identifier.
--!   @p1 (BIT) - Debug mode (1 prints the quoted statement; 0 executes it).
--! Security: QUOTENAME protects the dynamic queue identifier.
SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 SYSNAME = N'RuyaServicesMessageQueueQueue_test';
DECLARE @p1 BIT = 1;
*/

DECLARE @QueueName SYSNAME = @p0;
DECLARE @Debug BIT = COALESCE(@p1, 0);

IF NULLIF(@QueueName, N'') IS NULL
    THROW 51203, 'QueueName is required.', 1;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.service_queues AS service_queue
    WHERE service_queue.[name] = @QueueName
      AND service_queue.[schema_id] = SCHEMA_ID(N'dbo')
)
    THROW 51211, 'The requested test queue does not exist.', 1;

DECLARE @Sql NVARCHAR(MAX) =
    N'ALTER QUEUE [dbo].' + QUOTENAME(@QueueName)
    + N' WITH STATUS = OFF, POISON_MESSAGE_HANDLING (STATUS = ON);';
IF @Debug = 1
BEGIN
    PRINT @Sql;
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;
    EXEC sys.sp_executesql @Sql;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
