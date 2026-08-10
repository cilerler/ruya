# Ruya Common .NET Libraries

[![](https://img.shields.io/badge/stackoverflow-ruya-orange.svg?style=for-the-badge&logo=stackoverflow)](https://stackoverflow.com/questions/tagged/ruya)

<!--
[![](https://img.shields.io/github/v/release/cilerler/ruya?style=for-the-badge&logo=github)](https://github.com/cilerler/ruya/releases)
![](https://img.shields.io/github/downloads/cilerler/ruya/latest/total.svg?style=for-the-badge&logo=github&color=yellow)
-->

A collection of opinionated, observability-first .NET libraries for building production-grade services. Each package below links to its own README with installation, configuration, and usage details.

## Package Naming

`Ruya.*` packages fall into two categories:

- **Extensions to Microsoft libraries** build on the corresponding .NET platform area. For example, `Ruya.Diagnostics` extends the diagnostics stack built on `System.Diagnostics`, and `Ruya.Extensions.Hosting` extends `Microsoft.Extensions.Hosting`.
- **Ruya-defined capabilities** introduce abstractions where the Microsoft platform does not provide an equivalent. For example, `Ruya.Services.CloudStorage.Abstractions` defines Ruya's provider-neutral cloud-storage contracts.

## Contents

- [Package Naming](#package-naming)
- [Primitives & Testing](#primitives--testing)
- [Extensions](#extensions)
- [Diagnostics & Observability](#diagnostics--observability)
- [ASP.NET Core](#aspnet-core)
- [Data Access](#data-access)
- [System Utilities](#system-utilities)
- [Distributed Lock](#distributed-lock)
- [Message Queue](#message-queue)
- [Reliable Messaging](#reliable-messaging)
- [Cloud Storage](#cloud-storage)
- [Token Broker](#token-broker)

---

## Primitives & Testing

| Package | Description | NuGet |
|---------|-------------|-------|
| [Ruya.Primitives](src/Ruya.Primitives/README.md) | Common primitives, constants, and extensions used across the framework. | [![](https://img.shields.io/nuget/v/Ruya.Primitives.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Primitives) |
| [Ruya.Testing.Primitives](src/Ruya.Testing.Primitives/README.md) | Base classes and utilities (`TestBase`, `TestHost`) for unit and integration tests. | [![](https://img.shields.io/nuget/v/Ruya.Testing.Primitives.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Testing.Primitives) |

## Extensions

| Package | Description | NuGet |
|---------|-------------|-------|
| [Ruya.Extensions.Configuration](src/Ruya.Extensions.Configuration/README.md) | Configuration helpers including feature flags and startup loading. | [![](https://img.shields.io/nuget/v/Ruya.Extensions.Configuration.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Extensions.Configuration) |
| [Ruya.Extensions.DependencyInjection](src/Ruya.Extensions.DependencyInjection/README.md) | Service collection validation to ensure required services are registered. | [![](https://img.shields.io/nuget/v/Ruya.Extensions.DependencyInjection.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Extensions.DependencyInjection) |
| [Ruya.Extensions.Hosting](src/Ruya.Extensions.Hosting/README.md) | `WorkerBackgroundService` with Cron scheduling, idle backoff, retries, and observability. | [![](https://img.shields.io/nuget/v/Ruya.Extensions.Hosting.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Extensions.Hosting) |

## Diagnostics & Observability

| Package | Description | NuGet |
|---------|-------------|-------|
| [Ruya.Diagnostics.Abstractions](src/Ruya.Diagnostics.Abstractions/README.md) | Abstractions for the Ruya Diagnostics framework. | [![](https://img.shields.io/nuget/v/Ruya.Diagnostics.Abstractions.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Diagnostics.Abstractions) |
| [Ruya.Diagnostics](src/Ruya.Diagnostics/README.md) | Distributed tracing helpers and `ActivitySource` debugging tools. | [![](https://img.shields.io/nuget/v/Ruya.Diagnostics.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Diagnostics) |
| [Ruya.OpenTelemetry](src/Ruya.OpenTelemetry/README.md) | OpenTelemetry registration with sensible defaults for traces, metrics, and logs. | [![](https://img.shields.io/nuget/v/Ruya.OpenTelemetry.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.OpenTelemetry) |

## ASP.NET Core

| Package | Description | NuGet |
|---------|-------------|-------|
| [Ruya.AspNetCore.DataProtection.StackExchangeRedis](src/Ruya.AspNetCore.DataProtection.StackExchangeRedis/README.md) | Data Protection with Redis key persistence, plus tracing, metrics, and health checks. | [![](https://img.shields.io/nuget/v/Ruya.AspNetCore.DataProtection.StackExchangeRedis.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.AspNetCore.DataProtection.StackExchangeRedis) |
| [Ruya.AspNetCore.Diagnostics](src/Ruya.AspNetCore.Diagnostics) | Global exception handler for ASP.NET Core applications. | [![](https://img.shields.io/nuget/v/Ruya.AspNetCore.Diagnostics.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.AspNetCore.Diagnostics) |
| [Ruya.AspNetCore.Middleware](src/Ruya.AspNetCore.Middleware/README.md) | Middleware for adding application metadata (version, environment) to response headers. | [![](https://img.shields.io/nuget/v/Ruya.AspNetCore.Middleware.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.AspNetCore.Middleware) |

## Data Access

| Package | Description | NuGet |
|---------|-------------|-------|
| [Ruya.EntityFrameworkCore.SqlServer](src/Ruya.EntityFrameworkCore.SqlServer/README.md) | SQL Server extensions for EF Core: `BatchLock`, `BulkInsert`, and `ModelMetadataService`. | [![](https://img.shields.io/nuget/v/Ruya.EntityFrameworkCore.SqlServer.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.EntityFrameworkCore.SqlServer) |

## System Utilities

| Package | Description | NuGet |
|---------|-------------|-------|
| [Ruya.System.Xml.Serialization](src/Ruya.System.Xml.Serialization/README.md) | Thread-safe, cached `XmlSerializer` wrapper with a simple static API. | [![](https://img.shields.io/nuget/v/Ruya.System.Xml.Serialization.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.System.Xml.Serialization) |

## Distributed Lock

A provider-agnostic distributed locking abstraction with multiple backends.

| Package | Description | NuGet |
|---------|-------------|-------|
| [Ruya.Services.DistributedLock.Abstractions](src/Ruya.Services.DistributedLock.Abstractions/README.md) | Core `IDistributedLock` interface, options, and helpers. | [![](https://img.shields.io/nuget/v/Ruya.Services.DistributedLock.Abstractions.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.DistributedLock.Abstractions) |
| [Ruya.Services.DistributedLock](src/Ruya.Services.DistributedLock/README.md) | Base implementation with telemetry, health checks, and the "acquire, execute, release" pattern. | [![](https://img.shields.io/nuget/v/Ruya.Services.DistributedLock.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.DistributedLock) |
| [Ruya.Services.DistributedLock.InMemory](src/Ruya.Services.DistributedLock.InMemory/README.md) | `SemaphoreSlim`-based provider for testing and single-instance apps. | [![](https://img.shields.io/nuget/v/Ruya.Services.DistributedLock.InMemory.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.DistributedLock.InMemory) |
| [Ruya.Services.DistributedLock.MsSql](src/Ruya.Services.DistributedLock.MsSql/README.md) | SQL Server `sp_getapplock`-based provider. | [![](https://img.shields.io/nuget/v/Ruya.Services.DistributedLock.MsSql.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.DistributedLock.MsSql) |
| [Ruya.Services.DistributedLock.Redis](src/Ruya.Services.DistributedLock.Redis/README.md) | Redis-based provider (`SET NX PX` or RedLock). | [![](https://img.shields.io/nuget/v/Ruya.Services.DistributedLock.Redis.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.DistributedLock.Redis) |

## Message Queue

A provider-agnostic async messaging abstraction with unified API and middleware support.

| Package | Description | NuGet |
|---------|-------------|-------|
| [Ruya.Services.MessageQueue](src/Ruya.Services.MessageQueue/README.md) | Core abstractions, middleware pipeline, telemetry, and health checks. | [![](https://img.shields.io/nuget/v/Ruya.Services.MessageQueue.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.MessageQueue) |
| [Ruya.Services.MessageQueue.InMemory](src/Ruya.Services.MessageQueue.InMemory/README.md) | `System.Threading.Channels`-based provider for testing and single-process apps. | [![](https://img.shields.io/nuget/v/Ruya.Services.MessageQueue.InMemory.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.MessageQueue.InMemory) |
| [Ruya.Services.MessageQueue.MsSql](src/Ruya.Services.MessageQueue.MsSql/README.md) | SQL Server provider with Service Broker (default) and table-based modes. | [![](https://img.shields.io/nuget/v/Ruya.Services.MessageQueue.MsSql.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.MessageQueue.MsSql) |
| [Ruya.Services.MessageQueue.RabbitMq](src/Ruya.Services.MessageQueue.RabbitMq/README.md) | RabbitMQ provider with durable exchanges, delayed delivery, and priority queues. | [![](https://img.shields.io/nuget/v/Ruya.Services.MessageQueue.RabbitMq.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.MessageQueue.RabbitMq) |
| [Ruya.Services.MessageQueue.Redis](src/Ruya.Services.MessageQueue.Redis/README.md) | Redis Pub/Sub and Streams provider for low-latency messaging. | [![](https://img.shields.io/nuget/v/Ruya.Services.MessageQueue.Redis.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.MessageQueue.Redis) |

## Reliable Messaging

Transactional Outbox + Inbox primitives for reliable, persistence- and transport-agnostic messaging.

| Package | Description |
|---------|-------------|
| [Ruya.Services.ReliableMessaging](src/Ruya.Services.ReliableMessaging/README.md) | Core Outbox/Inbox abstractions with per-`TContext` registration. |
| [Ruya.Services.ReliableMessaging.EntityFrameworkCore](src/Ruya.Services.ReliableMessaging.EntityFrameworkCore/README.md) | EF Core storage adapter with a `SaveChangesAsync` interceptor for atomic commits. |
| [Ruya.Services.ReliableMessaging.MessageQueue](src/Ruya.Services.ReliableMessaging.MessageQueue/README.md) | `Ruya.Services.MessageQueue` transport adapter with consumer-side inbox dedup. |

## Cloud Storage

A unified, stateless interface for file storage across multiple cloud providers.

| Package | Description | NuGet |
|---------|-------------|-------|
| [Ruya.Services.CloudStorage.Abstractions](src/Ruya.Services.CloudStorage.Abstractions/README.md) | `ICloudFileService` and `ICloudStorageFactory` abstractions with built-in telemetry. | [![](https://img.shields.io/nuget/v/Ruya.Services.CloudStorage.Abstractions.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.CloudStorage.Abstractions) |
| [Ruya.Services.CloudStorage.Amazon](src/Ruya.Services.CloudStorage.Amazon/README.md) | Amazon S3 provider. | [![](https://img.shields.io/nuget/v/Ruya.Services.CloudStorage.Amazon.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.CloudStorage.Amazon) |
| [Ruya.Services.CloudStorage.Azure](src/Ruya.Services.CloudStorage.Azure/README.md) | Azure Blob Storage provider. | [![](https://img.shields.io/nuget/v/Ruya.Services.CloudStorage.Azure.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.CloudStorage.Azure) |
| [Ruya.Services.CloudStorage.Google](src/Ruya.Services.CloudStorage.Google/README.md) | Google Cloud Storage provider. | [![](https://img.shields.io/nuget/v/Ruya.Services.CloudStorage.Google.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.CloudStorage.Google) |
| [Ruya.Services.CloudStorage.Local](src/Ruya.Services.CloudStorage.Local/README.md) | Local file system provider for development and on-premise deployments. | [![](https://img.shields.io/nuget/v/Ruya.Services.CloudStorage.Local.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.CloudStorage.Local) |

## Token Broker

A JWT-based service-to-service authentication system with API key auth and nested actor chain token exchange (on-behalf-of flow).

| Package | Description | NuGet |
|---------|-------------|-------|
| [Ruya.Services.TokenBroker.Abstractions](src/Ruya.Services.TokenBroker.Abstractions) | Contracts, models, validation, and constants shared across the broker, client, and validation packages. | [![](https://img.shields.io/nuget/v/Ruya.Services.TokenBroker.Abstractions.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.TokenBroker.Abstractions) |
| [Ruya.Services.TokenBroker](src/Ruya.Services.TokenBroker/README.md) | The token broker service: issues and exchanges JWTs, backed by Redis for API keys. | [![](https://img.shields.io/nuget/v/Ruya.Services.TokenBroker.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.TokenBroker) |
| [Ruya.Services.TokenBroker.Client](src/Ruya.Services.TokenBroker.Client) | Client for requesting and exchanging tokens against the broker. | [![](https://img.shields.io/nuget/v/Ruya.Services.TokenBroker.Client.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.TokenBroker.Client) |
| [Ruya.Services.TokenBroker.Validation](src/Ruya.Services.TokenBroker.Validation) | JWT validation extensions and `ClaimsPrincipal` helpers for resource servers. | [![](https://img.shields.io/nuget/v/Ruya.Services.TokenBroker.Validation.svg?logo=nuget)](https://www.nuget.org/packages/Ruya.Services.TokenBroker.Validation) |
