-- Ruya.Services.MessageQueue SQL Server Service Broker Schema
-- This script sets up SQL Server Service Broker for native message queuing

-- Enable Service Broker on the database (if not already enabled)
-- Note: This requires ALTER DATABASE permission
-- IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = DB_NAME() AND is_broker_enabled = 1)
-- BEGIN
--     ALTER DATABASE CURRENT SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
-- END
-- GO

-- Check if Service Broker is enabled
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = DB_NAME() AND is_broker_enabled = 1)
BEGIN
    PRINT 'WARNING: Service Broker is not enabled on this database.'
    PRINT 'Run the following command with appropriate permissions:'
    PRINT 'ALTER DATABASE [' + DB_NAME() + '] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;'
END
GO

-- Create message type for our bus messages
-- Using VALIDATION = NONE for flexibility (we handle serialization in the app)
IF NOT EXISTS (SELECT * FROM sys.service_message_types WHERE name = 'RuyaServicesMessageQueueMessage')
BEGIN
    CREATE MESSAGE TYPE [RuyaServicesMessageQueueMessage] VALIDATION = NONE;
    PRINT 'Created message type: RuyaServicesMessageQueueMessage';
END
GO

-- Create contract for message exchange
IF NOT EXISTS (SELECT * FROM sys.service_contracts WHERE name = 'RuyaServicesMessageQueueContract')
BEGIN
    CREATE CONTRACT [RuyaServicesMessageQueueContract]
    (
        [RuyaServicesMessageQueueMessage] SENT BY ANY
    );
    PRINT 'Created contract: RuyaServicesMessageQueueContract';
END
GO

-- Create stored procedure to create a queue and service for a topic
CREATE OR ALTER PROCEDURE [dbo].[RuyaServicesMessageQueue_CreateTopicService]
    @TopicName NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @QueueName NVARCHAR(300) = 'RuyaServicesMessageQueueQueue_' + REPLACE(@TopicName, '.', '_');
    DECLARE @ServiceName NVARCHAR(300) = 'RuyaServicesMessageQueueService_' + REPLACE(@TopicName, '.', '_');
    DECLARE @SQL NVARCHAR(MAX);

    -- Create queue if not exists
    IF NOT EXISTS (SELECT * FROM sys.service_queues WHERE name = @QueueName)
    BEGIN
        SET @SQL = N'CREATE QUEUE [' + @QueueName + '] WITH STATUS = ON, RETENTION = OFF;';
        EXEC sp_executesql @SQL;
        PRINT 'Created queue: ' + @QueueName;
    END

    -- Create service if not exists
    IF NOT EXISTS (SELECT * FROM sys.services WHERE name = @ServiceName)
    BEGIN
        SET @SQL = N'CREATE SERVICE [' + @ServiceName + '] ON QUEUE [' + @QueueName + '] ([RuyaServicesMessageQueueContract]);';
        EXEC sp_executesql @SQL;
        PRINT 'Created service: ' + @ServiceName;
    END
END
GO

-- Create stored procedure to send a message
CREATE OR ALTER PROCEDURE [dbo].[RuyaServicesMessageQueue_SendMessage]
    @TopicName NVARCHAR(255),
    @MessagePayload NVARCHAR(MAX),
    @MessageId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ServiceName NVARCHAR(300) = 'RuyaServicesMessageQueueService_' + REPLACE(@TopicName, '.', '_');
    DECLARE @ConversationHandle UNIQUEIDENTIFIER;

    -- Ensure the service exists
    EXEC [dbo].[RuyaServicesMessageQueue_CreateTopicService] @TopicName;

    -- Begin a dialog conversation
    BEGIN DIALOG CONVERSATION @ConversationHandle
        FROM SERVICE @ServiceName
        TO SERVICE @ServiceName
        ON CONTRACT [RuyaServicesMessageQueueContract]
        WITH ENCRYPTION = OFF;

    -- Send the message
    SEND ON CONVERSATION @ConversationHandle
        MESSAGE TYPE [RuyaServicesMessageQueueMessage] (@MessagePayload);

    -- End the conversation (fire-and-forget pattern)
    END CONVERSATION @ConversationHandle;

    SET @MessageId = @ConversationHandle;
END
GO

-- Create stored procedure to receive messages
CREATE OR ALTER PROCEDURE [dbo].[RuyaServicesMessageQueue_ReceiveMessages]
    @TopicName NVARCHAR(255),
    @BatchSize INT = 1,
    @WaitTimeMs INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @QueueName NVARCHAR(300) = 'RuyaServicesMessageQueueQueue_' + REPLACE(@TopicName, '.', '_');
    DECLARE @SQL NVARCHAR(MAX);

    -- Ensure the service exists
    EXEC [dbo].[RuyaServicesMessageQueue_CreateTopicService] @TopicName;

    -- Build dynamic SQL for RECEIVE (queue name must be dynamic)
    IF @WaitTimeMs > 0
    BEGIN
        SET @SQL = N'
            WAITFOR (
                RECEIVE TOP(' + CAST(@BatchSize AS NVARCHAR(10)) + ')
                    conversation_handle,
                    message_type_name,
                    message_body,
                    message_sequence_number
                FROM [' + @QueueName + ']
            ), TIMEOUT ' + CAST(@WaitTimeMs AS NVARCHAR(10)) + ';';
    END
    ELSE
    BEGIN
        SET @SQL = N'
            RECEIVE TOP(' + CAST(@BatchSize AS NVARCHAR(10)) + ')
                conversation_handle,
                message_type_name,
                message_body,
                message_sequence_number
            FROM [' + @QueueName + '];';
    END

    EXEC sp_executesql @SQL;
END
GO

-- Create stored procedure to acknowledge/end conversations
CREATE OR ALTER PROCEDURE [dbo].[RuyaServicesMessageQueue_EndConversation]
    @ConversationHandle UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- End the conversation (cleanup)
    IF EXISTS (SELECT * FROM sys.conversation_endpoints WHERE conversation_handle = @ConversationHandle)
    BEGIN
        END CONVERSATION @ConversationHandle;
    END
END
GO

-- Create stored procedure to get queue statistics
CREATE OR ALTER PROCEDURE [dbo].[RuyaServicesMessageQueue_GetQueueStats]
    @TopicName NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @QueueName NVARCHAR(300) = 'RuyaServicesMessageQueueQueue_' + REPLACE(@TopicName, '.', '_');

    SELECT
        q.name AS QueueName,
        q.is_receive_enabled AS IsReceiveEnabled,
        q.is_enqueue_enabled AS IsEnqueueEnabled,
        ISNULL(qs.messages_in_queue, 0) AS MessagesInQueue,
        s.name AS ServiceName
    FROM sys.service_queues q
    LEFT JOIN sys.dm_broker_queue_monitors qs ON q.object_id = qs.queue_id
    LEFT JOIN sys.services s ON s.service_queue_id = q.object_id
    WHERE q.name = @QueueName;
END
GO

-- Create table for tracking dead letter messages (Service Broker doesn't have built-in DLQ)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'RuyaServicesMessageQueueDeadLetter')
BEGIN
    CREATE TABLE [dbo].[RuyaServicesMessageQueueDeadLetter]
    (
        [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [MessageId] UNIQUEIDENTIFIER NOT NULL,
        [TopicName] NVARCHAR(255) NOT NULL,
        [MessagePayload] NVARCHAR(MAX) NOT NULL,
        [ErrorMessage] NVARCHAR(MAX) NULL,
        [DeliveryAttempts] INT NOT NULL,
        [OriginalTimestamp] DATETIME2 NOT NULL,
        [DeadLetterTimestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        INDEX IX_RuyaServicesMessageQueueDeadLetter_TopicName ([TopicName]),
        INDEX IX_RuyaServicesMessageQueueDeadLetter_Timestamp ([DeadLetterTimestamp])
    );
    PRINT 'Created dead letter table';
END
GO

PRINT 'Ruya.Services.MessageQueue Service Broker schema setup complete';
PRINT 'Note: Ensure Service Broker is enabled on your database';
