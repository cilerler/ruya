--! Ruya MessageQueue tests - Count application messages in a test-owned Service Broker queue.
--! Parameters:
--!   @p0 (SYSNAME) - Test-owned queue identifier.
--!   @p1 (NVARCHAR(256)) - Application message type.
--!   @p2 (BIGINT OUTPUT) - Matching message count.
--!   @p3 (BIT) - Debug mode (1 prints the operation without reading the queue; 0 executes it).
--! Security: QUOTENAME protects the queue identifier; no message body is read or printed.
SET NOCOUNT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 SYSNAME = N'RuyaServicesMessageQueueQueue_test';
DECLARE @p1 NVARCHAR(256) = N'RuyaServicesMessageQueueMessage';
DECLARE @p2 BIGINT;
DECLARE @p3 BIT = 1;
*/

DECLARE @QueueName SYSNAME = @p0;
DECLARE @MessageType NVARCHAR(256) = @p1;
DECLARE @MessageCount BIGINT;
DECLARE @Debug BIT = COALESCE(@p3, 0);

IF NULLIF(@QueueName, N'') IS NULL OR NULLIF(@MessageType, N'') IS NULL
    THROW 51200, 'QueueName and MessageType are required.', 1;

IF @Debug = 1
BEGIN
    PRINT N'Ruya MessageQueue tests would count application messages in the supplied queue.';
    SET @p2 = 0;
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.service_queues AS service_queue
    WHERE service_queue.[name] = @QueueName
      AND service_queue.[schema_id] = SCHEMA_ID(N'dbo')
)
    THROW 51214, 'The requested test queue does not exist.', 1;

DECLARE @Sql NVARCHAR(MAX) =
    N'SELECT @MessageCount = COUNT_BIG(*) '
    + N'FROM [dbo].' + QUOTENAME(@QueueName) + N' AS queued_message '
    + N'WHERE queued_message.[message_type_name] = @MessageType;';
EXEC sys.sp_executesql
    @Sql,
    N'@MessageType NVARCHAR(256), @MessageCount BIGINT OUTPUT',
    @MessageType = @MessageType,
    @MessageCount = @MessageCount OUTPUT;

SET @p2 = @MessageCount;
