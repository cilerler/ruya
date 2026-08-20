# Ruya.System.Xml.Serialization

Thread-safe, cached XML serialization helpers for .NET.

## Features

-   **Thread-Safe**: Safely use `XmlSerializer` in parallel scenarios.
-   **Cached**: Caches serializer instances to avoid memory leaks and performance hits.
-   **Simple API**: Static `Xml.Serialize` and `Xml.Deserialize` methods.

## Usage

### Basic

```csharp
var xml = Xml.Serialize(myObject);
var obj = Xml.Deserialize<MyType>(xml);
```

### Parallel Usage

```csharp
await Parallel.ForEachAsync(records, new ParallelOptions 
{
    MaxDegreeOfParallelism = Environment.ProcessorCount
}, async (record, ct) =>
{
    // Safe to call concurrently
    var xml = Xml.Serialize(record);
    await ProcessAsync(xml, ct);
});
```

**PLINQ Usage**

```csharp
var results = records
    .AsParallel()
    .WithDegreeOfParallelism(Environment.ProcessorCount)
    .Select(record => Xml.Deserialize<MyType>(record.Xml))
    .ToList();
```

## Why?

Standard `XmlSerializer` has memory leak issues if not used carefully (e.g., when using non-standard constructors). This library manages the caching and thread safety for you.

```
Thread 1 ──┬── GetSerializer<Order>() ──┐
Thread 2 ──┤                            ├── Same cached Lazy<XmlSerializer>
Thread 3 ──┤                            │   (created once, shared safely)
Thread 4 ──┘                            │
                                        ▼
                               ┌─────────────────┐
                               │ XmlSerializer   │ (single instance)
                               │ (thread-safe    │
                               │  after creation)│
                               └─────────────────┘
```
