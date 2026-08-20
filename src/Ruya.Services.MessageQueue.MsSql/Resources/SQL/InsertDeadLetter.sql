--! Ruya MessageQueue - Retain a terminally failed delivery in the dead-letter table.
--! Parameters:
--!   @p0 (NVARCHAR) - Provider-neutral message identifier.
--!   @p1 (NVARCHAR(255)) - Logical topic.
--!   @p2 (VARBINARY(MAX)) - Exact serialized envelope bytes.
--!   @p3 (NVARCHAR) - Terminal failure reason.
--!   @p4 (INT) - Applied delivery count.
--!   @p5 (DATETIME2) - Original envelope timestamp.
--!   @p6 (BIT) - Debug mode (1 prints the operation without inserting; 0 executes it).
--! Security: Values are parameterized and no payload or credential is printed.
--! Transaction: Runs inside the caller-owned receive transaction.
SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 NVARCHAR(MAX) = N'message-id';
DECLARE @p1 NVARCHAR(255) = N'orders.created';
DECLARE @p2 VARBINARY(MAX) = 0x00;
DECLARE @p3 NVARCHAR(MAX) = N'test failure';
DECLARE @p4 INT = 1;
DECLARE @p5 DATETIME2 = SYSUTCDATETIME();
DECLARE @p6 BIT = 1;
*/

DECLARE @MessageId NVARCHAR(MAX) = @p0;
DECLARE @TopicName NVARCHAR(255) = @p1;
DECLARE @MessagePayload VARBINARY(MAX) = @p2;
DECLARE @ErrorMessage NVARCHAR(MAX) = @p3;
DECLARE @DeliveryAttempts INT = @p4;
DECLARE @OriginalTimestamp DATETIME2 = @p5;
DECLARE @Debug BIT = COALESCE(@p6, 0);

IF NULLIF(@MessageId, N'') IS NULL OR NULLIF(@TopicName, N'') IS NULL
    THROW 51104, 'MessageId and TopicName are required.', 1;
IF @MessagePayload IS NULL
    THROW 51105, 'MessagePayload is required.', 1;
IF @DeliveryAttempts < 0
    THROW 51106, 'DeliveryAttempts cannot be negative.', 1;

IF @Debug = 1
BEGIN
    PRINT N'Ruya MessageQueue would insert one dead-letter record.';
    RETURN;
END;

IF OBJECT_ID(N'dbo.RuyaServicesMessageQueueDeadLetter', N'U') IS NULL
    THROW 51113, 'The Ruya dead-letter table is not installed.', 1;

DECLARE @StartedTransaction BIT = 0;
IF @@TRANCOUNT = 0
BEGIN
    BEGIN TRANSACTION;
    SET @StartedTransaction = 1;
END;

BEGIN TRY
    INSERT INTO [dbo].[RuyaServicesMessageQueueDeadLetter]
    (
        [MessageId],
        [TopicName],
        [MessagePayload],
        [ErrorMessage],
        [DeliveryAttempts],
        [OriginalTimestamp]
    )
    VALUES
    (
        @MessageId,
        @TopicName,
        @MessagePayload,
        @ErrorMessage,
        @DeliveryAttempts,
        @OriginalTimestamp
    );

    IF @StartedTransaction = 1
        COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @StartedTransaction = 1 AND @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
