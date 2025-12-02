# Ruya.Services.MessageQueue.MsSql

SQL Server message bus provider for Ruya.Services.MessageQueue framework.

## Features

This provider supports **two messaging approaches**:

### 1. **SQL Server Service Broker** (Recommended, Default)
Native SQL Server messaging infrastructure with:
- Asynchronous message delivery
- Transactional messaging with guaranteed delivery
- Priority-based message processing (1-10, 1=highest)
- Automatic poison message handling
- Conversation-based messaging
- Native queuing with WAITFOR (blocking receive)
- No polling overhead

**Pros:**
- Higher performance (push-based, no polling)
- Native transactional support
- Automatic retry and poison message handling
- Better scalability

**Cons:**
- Requires Service Broker to be enabled (`ALTER DATABASE SET ENABLE_BROKER`)
- More complex setup and troubleshooting
- Requires understanding of Service Broker concepts

### 2. **Table-Based Polling** (Fallback)
Traditional approach using regular tables:
- Messages stored in a table with status tracking
- Subscribers poll the table periodically
- Uses `UPDLOCK, READPAST` for atomic dequeuing
- Exponential backoff for retries
- Adaptive polling (fast when busy, slow when idle)

**Pros:**
- Simpler to understand and debug
- Works with any SQL Server edition
- No special configuration required
- Familiar table-based approach

**Cons:**
- Polling overhead
- Higher latency
- More database load

## Configuration

```csharp
services.AddMessageQueue()
    .AddMsSql(options =>
    {
        // Connection string (required)
        options.ConnectionString = "Server=localhost;Database=MyDb;Integrated Security=true;";

        // Use Service Broker (default: true)
        options.UseServiceBroker = true;

        // Service Broker settings (when UseServiceBroker = true)
        options.ServiceBrokerPriority = 5;      // 1-10, 1=highest
        options.ReceiveTimeoutMs = 1000;        // WAITFOR timeout

        // Table-based settings (when UseServiceBroker = false)
        options.SchemaName = "dbo";
        options.TableName = "MessageQueue";
        options.PollingIntervalMs = 100;
        options.MaxPollingIntervalMs = 5000;
        options.BatchSize = 10;

        // Common settings
        options.MaxDeliveryAttempts = 5;
        options.AutoCreateSchema = true;
        options.CommandTimeoutSeconds = 30;
    });
```

## Service Broker Setup

### Enable Service Broker

```sql
-- Check if Service Broker is enabled
SELECT is_broker_enabled FROM sys.databases WHERE name = 'YourDatabase';

-- Enable Service Broker (requires exclusive access)
ALTER DATABASE [YourDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
ALTER DATABASE [YourDatabase] SET ENABLE_BROKER;
ALTER DATABASE [YourDatabase] SET MULTI_USER;
```

### Schema Creation

The provider automatically creates the necessary schema when `AutoCreateSchema = true`:
- Message types (`RuyaServicesMessageQueueMessage`, `RuyaServicesMessageQueueEndDialog`)
- Contract (`RuyaServicesMessageQueueContract`)
- System queue and service
- Stored procedures for sending and receiving messages
- Dead letter table

Topics are created dynamically when first used (queue + service per topic).

## Usage

### Publishing

```csharp
var queue = await messageQueueFactory.CreateQueueAsync("mssql");

// Simple publish
await queue.PublishAsync("orders", new OrderCreatedEvent { OrderId = "123" });

// With options
await queue.PublishAsync("orders", orderEvent, new PublishOptions
{
    Priority = 1,              // Highest priority
    DelayedDelivery = TimeSpan.FromMinutes(5),
    Headers = new Dictionary<string, string>
    {
        ["CorrelationId"] = "correlation-123"
    }
});

// Batch publishing (transactional)
await queue.PublishBatchAsync("orders", orderEvents);
```

### Subscribing

```csharp
var subscription = await queue.SubscribeAsync<OrderCreatedEvent>(
    "orders",
    async context =>
    {
        var order = context.Envelope.Body;
        Console.WriteLine($"Processing order: {order.OrderId}");

        // Process the message
        await ProcessOrderAsync(order);

        return MessageResult.Success();
    },
    new SubscribeOptions
    {
        MaxConcurrency = 5,
        PrefetchCount = 10
    });

// Pause/resume subscription
await subscription.PauseAsync();
await subscription.ResumeAsync();

// Dispose when done
await subscription.DisposeAsync();
```

## Architecture

### Service Broker Flow

```
Publisher → sp_SendMessage → Service Broker Queue → RECEIVE (Subscriber) → Handler
                                  ↓
                            Dead Letter (on max retries)
```

1. **Publish**: Calls `sp_SendMessage` stored procedure
2. **Create Conversation**: BEGIN DIALOG creates a conversation
3. **Send Message**: SEND ON CONVERSATION adds message to queue with priority
4. **End Conversation**: Immediately ends dialog (one-way messaging)
5. **Receive**: Subscriber uses WAITFOR RECEIVE (blocking, efficient)
6. **Process**: Handler processes message
7. **Acknowledge**: END CONVERSATION removes message from queue
8. **Retry**: On failure, message stays in queue or moves to DLQ

### Table-Based Flow

```
Publisher → INSERT → Table → Polling (UPDATE + SELECT) → Handler
                      ↓
                Status: Pending → Processing → Processed/Failed
```

1. **Publish**: INSERT message with Status='Pending'
2. **Poll**: Periodic query with UPDLOCK, READPAST
3. **Lock**: UPDATE Status='Processing', set LockedUntil
4. **Process**: Handler processes message
5. **Acknowledge**: UPDATE Status='Processed' or retry with exponential backoff
6. **Dead Letter**: After max attempts, move to dead letter table

## Provider Capabilities

```csharp
SupportsPriority = true          // Service Broker: 1-10, Table: INT
SupportsDelayedDelivery = true   // Service Broker: via AvailableAt, Table: AvailableAt timestamp
SupportsTimeToLive = false       // Would require background cleanup
SupportsPublisherConfirms = true // Via transaction
SupportsConsumerGroups = false   // Competing consumers share same queue
SupportsDeadLetterQueue = true   // Automatic (Service Broker) or manual (Table)
SupportsReplay = false           // Messages are processed once
SupportsBatchPublish = true      // Transaction-based batching
```

## Performance Considerations

### Service Broker
- **Throughput**: Very high (push-based)
- **Latency**: Low (WAITFOR blocks until message arrives)
- **Database Load**: Low (no polling)
- **Scalability**: Excellent (native SQL Server feature)

### Table-Based
- **Throughput**: Medium (polling-based)
- **Latency**: Higher (depends on polling interval)
- **Database Load**: Higher (continuous polling)
- **Scalability**: Good (but limited by polling overhead)

### Recommendations
- **Service Broker**: Production workloads, high throughput, low latency
- **Table-Based**: Development, debugging, simple scenarios, or when Service Broker can't be enabled

## Troubleshooting

### Service Broker Not Enabled

**Error**: "Service Broker is not enabled on database"

**Solution**:
```sql
ALTER DATABASE [YourDb] SET ENABLE_BROKER;
```

### Exclusive Access Required

**Error**: "ALTER DATABASE failed because a lock could not be placed on database"

**Solution**:
```sql
ALTER DATABASE [YourDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
ALTER DATABASE [YourDb] SET ENABLE_BROKER;
ALTER DATABASE [YourDb] SET MULTI_USER;
```

### Messages Not Being Received

**Check**:
1. Service Broker enabled: `SELECT is_broker_enabled FROM sys.databases WHERE name = 'YourDb'`
2. Queue status: `SELECT name, is_receive_enabled FROM sys.service_queues`
3. Messages in queue: `SELECT COUNT(*) FROM sys.transmission_queue`
4. Conversations: `SELECT * FROM sys.conversation_endpoints`

### Poison Messages

Service Broker automatically handles poison messages (messages that fail repeatedly):
- After 5 failures, message is moved to poison message queue
- Check: `SELECT * FROM sys.service_queues WHERE is_poison_message_handling_enabled = 1`

## Migration from Table-Based to Service Broker

```csharp
// Old configuration
options.UseServiceBroker = false;
options.PollingIntervalMs = 100;

// New configuration
options.UseServiceBroker = true;
options.ServiceBrokerPriority = 5;
options.ReceiveTimeoutMs = 1000;

// Note: Enable Service Broker on database first
```
