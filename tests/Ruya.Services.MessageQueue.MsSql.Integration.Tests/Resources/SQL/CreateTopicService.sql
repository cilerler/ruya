--! Ruya MessageQueue tests - Resolve and provision test topic topology.
--! Parameters:
--!   @p0 (NVARCHAR(255)) - Logical topic name.
--!   @p1, @p2 (SYSNAME) - Collision-safe queue and service names.
--!   @p3, @p4 (SYSNAME) - Optional legacy queue and service names.
--!   @p5, @p6 (SYSNAME OUTPUT) - Resolved queue and service names.
--!   @p7 (BIT) - Debug mode (1 prints the operation without provisioning; 0 executes it).
--! Security: The production procedure validates ownership and quotes dynamic identifiers.
SET NOCOUNT ON;

/*
-- DEBUG: Uncomment this block to test the script in SSMS.
DECLARE @p0 NVARCHAR(255) = N'test.topic';
DECLARE @p1 SYSNAME = N'RuyaServicesMessageQueueQueue_test';
DECLARE @p2 SYSNAME = N'RuyaServicesMessageQueueService_test';
DECLARE @p3 SYSNAME = NULL;
DECLARE @p4 SYSNAME = NULL;
DECLARE @p5 SYSNAME;
DECLARE @p6 SYSNAME;
DECLARE @p7 BIT = 1;
*/

DECLARE @TopicName NVARCHAR(255) = @p0;
DECLARE @ProposedQueueName SYSNAME = @p1;
DECLARE @ProposedServiceName SYSNAME = @p2;
DECLARE @LegacyQueueName SYSNAME = @p3;
DECLARE @LegacyServiceName SYSNAME = @p4;
DECLARE @ResolvedQueueName SYSNAME;
DECLARE @ResolvedServiceName SYSNAME;
DECLARE @Debug BIT = COALESCE(@p7, 0);

IF NULLIF(@TopicName, N'') IS NULL
    THROW 51204, 'TopicName is required.', 1;
IF NULLIF(@ProposedQueueName, N'') IS NULL OR NULLIF(@ProposedServiceName, N'') IS NULL
    THROW 51205, 'Proposed queue and service names are required.', 1;

IF @Debug = 1
BEGIN
    PRINT N'Ruya MessageQueue tests would resolve and provision test topic topology.';
    RETURN;
END;

IF OBJECT_ID(N'dbo.RuyaServicesMessageQueue_CreateTopicService', N'P') IS NULL
    THROW 51212, 'The Ruya topic-provisioning procedure is not installed.', 1;

EXEC [dbo].[RuyaServicesMessageQueue_CreateTopicService]
    @TopicName = @TopicName,
    @ProposedQueueName = @ProposedQueueName,
    @ProposedServiceName = @ProposedServiceName,
    @LegacyQueueName = @LegacyQueueName,
    @LegacyServiceName = @LegacyServiceName,
    @ResolvedQueueName = @ResolvedQueueName OUTPUT,
    @ResolvedServiceName = @ResolvedServiceName OUTPUT;

SET @p5 = @ResolvedQueueName;
SET @p6 = @ResolvedServiceName;
