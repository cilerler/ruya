# Ruya Token Service

A JWT-based service-to-service authentication system with API key authentication and token exchange (on-behalf-of flow) supporting **nested actor chains**.

## Architecture

```
┌──────────┐   API Key    ┌───────────────┐
│ Service A │────────────▶│ Token Service │◀──── Redis (API keys)
└────┬─────┘              └───────┬───────┘
     │                            │
     │ JWT                        │ JWT
     ▼                            ▼
┌──────────┐   Exchange   ┌──────────┐   Exchange   ┌──────────┐
│ Service B │────────────▶│ Service C │────────────▶│ Service N │
└──────────┘              └──────────┘              └──────────┘
     │                         │                         │
     └───────────── Full Actor Chain Preserved ──────────┘
```

## Nested Actor Chains (RFC 8693)

When tokens are exchanged through multiple services, the **full chain is preserved**:

```
User → API → Service1 → Service2 → Service-N → Target
```

Service-N's token contains:
```json
{
  "sub": "user-123",
  "act": {
    "sub": "service-n",
    "act": {
      "sub": "service2",
      "act": {
        "sub": "service1"
      }
    }
  }
}
```

**Scenario 1: Token observed by Target after a user request traverses the services**
- `GetOriginalSubject()` → "user-123"
- `GetActorChainList()` → ["service-n", "service2", "service1"]
- `GetImmediateActor()` → "service-n" (who called us)
- `GetOriginalActor()` → "service1" (first service after user)

**Scenario 2: Token observed by Target after a cronjob/background job traverses the services**
- `GetOriginalSubject()` → "service1" (the cronjob owner)
- `GetActorChainList()` → ["service-n", "service2"]
- Service-N knows service1 initiated the request

## Library Structure

```
┌─────────────────────────────────────────────────────────────────────┐
│                          Your Services                              │
├───────────────────────┬───────────────────────┬─────────────────────┤
│   Central Auth        │   Microservices       │   Validators Only   │
│   (Token Issuer)      │   (Need Tokens)       │   (Just Validate)   │
├───────────────────────┼───────────────────────┼─────────────────────┤
│ TokenBroker          │ TokenBroker.Client   │ TokenBroker.       │
│ (full)                │                       │ Validation          │
├───────────────────────┴───────────────────────┴─────────────────────┤
│                    TokenBroker.Abstractions                        │
│              (Models, Constants, Interfaces - shared by all)        │
└─────────────────────────────────────────────────────────────────────┘
```

| Package | Purpose | Dependencies |
|---------|---------|--------------|
| **Ruya.Services.TokenBroker.Abstractions** | Shared models, constants, interfaces | None |
| **Ruya.Services.TokenBroker.Validation** | Lightweight JWT validation only | Ruya.Services.TokenBroker.Abstractions |
| **Ruya.Services.TokenBroker.Client** | HTTP client to request/exchange tokens | Ruya.Services.TokenBroker.Abstractions |
| **Ruya.Services.TokenBroker** | Full service: issuing, validation, distributed-cache API keys | Ruya.Services.TokenBroker.Abstractions, Ruya.Services.TokenBroker.Validation |

## Quick Start

### 1. Generate an RSA signing-key pair

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out token-broker-private.pem
openssl pkey -in token-broker-private.pem -pubout -out token-broker-public.pem
```

The issuer receives the private PEM from a secret provider. Validators receive only the public PEM. Never distribute the private key to validator services.

### 2. Configure appsettings.json

```json
{
  "TokenBroker": {
    "Issuer": "ruya-token-service",
    "Audiences": [
      "ruya-api",
      "ruya-services"
    ],
    "SigningKeyId": "token-key-2026-08",
    "SigningPublicKeys": {
      "token-key-2026-08": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----"
    },
    "TokenLifetime": "00:15:00",
    "ClockSkew": "00:00:30",
    "ApiKeyCacheDuration": "00:05:00"
  },
  "DistributedLock": {
    "Redis": {
      "ConnectionStringKey": "Redis"
    }
  }
}

```

### 3. Register in Program.cs

**Token Service (issuer):**
```csharp
using Ruya.Services.DistributedLock.Redis.Extensions;
using Ruya.Services.TokenBroker;

var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is required from the active secret provider.");
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
builder.Services.AddRedisDistributedLock();
builder.Services.AddTokenBroker();
app.MapTokenBrokerApi();
```

Supply `TokenBroker__SigningPrivateKeyPem` and `ConnectionStrings__Redis` through environment-backed secrets, a vault provider, or user-secrets for local development. Do not place either value in ordinary appsettings files. The public-key ring must contain the public half of the active `SigningKeyId`; startup fails when they do not match. `IDistributedLock` is required because API-key registration and rotation must be serialized across replicas.

**Service A (caller):**
```csharp
using Ruya.Services.TokenBroker;
using Ruya.Services.TokenBroker.Client;

builder.Services.AddTokenClient();
builder.Services.AddTokenValidation();
```

**Service B/C (validators only):**
```csharp
using Ruya.Services.TokenBroker;

builder.Services.AddTokenValidation();
```

Validator configuration contains `Issuer`, `Audiences`, `ClockSkew`, and `SigningPublicKeys`; it must never contain `SigningPrivateKeyPem` or the obsolete `SigningKeyBase64` value.

## Token Renewal

The `TokenClient` handles token caching and renewal automatically:

```csharp
// TokenClient automatically:
// 1. Caches tokens in memory
// 2. Refreshes 1 minute before expiry (configurable TokenRefreshBuffer)
// 3. Thread-safe with SemaphoreSlim to prevent thundering herd

var token = await tokenClient.GetTokenAsync(cancellationToken: ct);
// Returns cached token or fetches new one if expired/expiring soon
```

**How it works internally:**
1. First call: requests new token from Token Service
2. Token cached with expiry time
3. Subsequent calls return cached token
4. When `ExpiresAt - RefreshBuffer < Now`: fetches new token
5. Cache is thread-safe for concurrent requests

**Configuration:**
```json
{
  "TokenClient": {
    "TokenBrokerUrl": "https://token-service",
    "ServiceName": "service-a",
    "TokenRefreshBuffer": "00:01:00"
  }
}
```

Supply `TokenClient__ApiKey` through a secret provider. HTTPS is required outside loopback. `AllowInsecureHttpForDevelopment` is an explicit development-only escape hatch and must not be enabled in production settings.

## Resilience & Fault Tolerance

The `TokenClient` uses the standard `Microsoft.Extensions.Http.Resilience` pipeline:

- **Retry**: bounded retries for transient failures with exponential backoff and jitter on retry-safe HTTP methods
- **Circuit Breaker**: failure-ratio sampling prevents sustained calls to an unhealthy issuer
- **Timeouts**: both per-attempt and total-request timeouts are enforced

Token creation and exchange are POST operations that issue credentials. They are deliberately not retried automatically because the API does not implement an idempotency-key contract; a lost response must not create additional valid JWTs invisibly. Use an `AddTokenClient` overload that accepts `Action<HttpStandardResilienceOptions>` when the service needs values different from the package defaults. Custom settings cannot re-enable retries for unsafe methods.

This ensures your services gracefully handle Token Service outages:

```
Token Service goes down for 5 minutes:
├── Services with valid cached tokens → Continue working normally
├── Services needing refresh → Make one issuance attempt, then fail without creating a duplicate token
└── Token Service recovers → Next request succeeds, services auto-recover
```

### Default Behavior

```csharp
// Resilience is enabled by default
builder.Services.AddTokenClient();
```

### Custom Resilience Configuration

```csharp
builder.Services.AddTokenClient(options =>
{
    // Customize retry for retry-safe methods only; token POSTs remain single-attempt.
    options.Retry.MaxRetryAttempts = 5;
    options.Retry.Delay = TimeSpan.FromSeconds(2);

    // Customize circuit breaker
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 10;

    // Customize timeout
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
});
```

### Resilience Metrics

The resilience handler automatically emits metrics:
- `http.client.request.duration` - Request duration histogram
- `http.client.open_connections` - Open connection count
- `resilience.polly.execution.attempt.duration` - Per-attempt duration

## Usage Scenarios

### Making Authenticated HTTP Calls

```csharp
public class OrderService
{
    private readonly ITokenClient _tokenClient;
    private readonly HttpClient _httpClient;

    public OrderService(ITokenClient tokenClient, HttpClient httpClient)
    {
        _tokenClient = tokenClient;
        _httpClient = httpClient;
    }

    public async Task<Order> GetOrderAsync(int orderId, CancellationToken ct)
    {
        // Get token (cached/auto-renewed)
        var token = await _tokenClient.GetTokenAsync(cancellationToken: ct);

        // Add to Authorization header
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.GetAsync($"/api/orders/{orderId}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Order>(ct);
    }
}
```

### Handling Token Errors with Retry

```csharp
public async Task<Order> GetOrderWithRetryAsync(int orderId, CancellationToken ct)
{
    var token = await _tokenClient.GetTokenAsync(cancellationToken: ct);

    for (int attempt = 0; attempt < 2; attempt++)
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.GetAsync($"/api/orders/{orderId}", ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
        {
            // Token might be stale - force refresh and retry once
            token = await _tokenClient.GetTokenAsync(forceRefresh: true, cancellationToken: ct);
            continue;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Order>(ct);
    }

    throw new InvalidOperationException("Failed after retry");
}
```

### Integrating with Kiota-Generated Clients

[Kiota](https://learn.microsoft.com/en-us/openapi/kiota/) is Microsoft's OpenAPI client generator. To use TokenBroker with Kiota clients, create an `IAccessTokenProvider` adapter:

**1. Create the Token Provider Adapter**

```csharp
using Microsoft.Kiota.Abstractions.Authentication;

public class TokenBrokerAccessTokenProvider : IAccessTokenProvider
{
    private readonly ITokenClient _tokenClient;
    private readonly string[]? _scopes;

    public TokenBrokerAccessTokenProvider(ITokenClient tokenClient, string[]? scopes = null)
    {
        _tokenClient = tokenClient;
        _scopes = scopes;
    }

    public AllowedHostsValidator AllowedHostsValidator { get; } = new();

    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        // Optionally check AllowedHostsValidator.IsUrlHostValid(uri) here
        return await _tokenClient.GetTokenAsync(_scopes, cancellationToken: cancellationToken);
    }
}
```

**2. Register the Kiota Client with DI**

```csharp
// In Program.cs or Startup.cs
builder.Services.AddTokenClient();

builder.Services.AddScoped<IAccessTokenProvider>(sp =>
    new TokenBrokerAccessTokenProvider(
        sp.GetRequiredService<ITokenClient>(),
        scopes: ["read:orders", "write:orders"]));

builder.Services.AddScoped<IAuthenticationProvider, BaseBearerTokenAuthenticationProvider>(sp =>
    new BaseBearerTokenAuthenticationProvider(sp.GetRequiredService<IAccessTokenProvider>()));

builder.Services.AddHttpClient<OrderServiceClient>()
    .AddTypedClient((httpClient, sp) =>
    {
        var authProvider = sp.GetRequiredService<IAuthenticationProvider>();
        var requestAdapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        requestAdapter.BaseUrl = "https://order-service.internal";
        return new OrderServiceClient(requestAdapter);
    });
```

**3. Use the Kiota Client**

```csharp
public class OrderHandler
{
    private readonly OrderServiceClient _client;

    public OrderHandler(OrderServiceClient client)
    {
        _client = client;
    }

    public async Task<Order?> GetOrderAsync(int orderId, CancellationToken ct)
    {
        // Token is automatically injected by the authentication provider
        return await _client.Orders[orderId].GetAsync(cancellationToken: ct);
    }

    public async Task<IEnumerable<Order>?> ListOrdersAsync(CancellationToken ct)
    {
        return await _client.Orders.GetAsync(cancellationToken: ct);
    }
}
```

**Alternative: Factory Pattern for Multiple Services**

When calling multiple services with different scopes:

```csharp
public interface IKiotaClientFactory
{
    TClient CreateClient<TClient>(string baseUrl, string[] scopes)
        where TClient : class;
}

public class KiotaClientFactory : IKiotaClientFactory
{
    private readonly ITokenClient _tokenClient;
    private readonly IHttpClientFactory _httpClientFactory;

    public KiotaClientFactory(ITokenClient tokenClient, IHttpClientFactory httpClientFactory)
    {
        _tokenClient = tokenClient;
        _httpClientFactory = httpClientFactory;
    }

    public TClient CreateClient<TClient>(string baseUrl, string[] scopes)
        where TClient : class
    {
        var tokenProvider = new TokenBrokerAccessTokenProvider(_tokenClient, scopes);
        var authProvider = new BaseBearerTokenAuthenticationProvider(tokenProvider);
        var httpClient = _httpClientFactory.CreateClient();
        var requestAdapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient)
        {
            BaseUrl = baseUrl
        };

        return (TClient)Activator.CreateInstance(typeof(TClient), requestAdapter)!;
    }
}

// Usage
var ordersClient = factory.CreateClient<OrderServiceClient>(
    "https://order-service.internal",
    ["read:orders"]);

var inventoryClient = factory.CreateClient<InventoryServiceClient>(
    "https://inventory-service.internal",
    ["read:inventory"]);
```

### Token Exchange (On-Behalf-Of)

When Service A calls Service B on behalf of a user:

```csharp
// Service A receives request with user token
app.MapGet("/api/data", async (HttpContext ctx, ITokenClient tokenClient) =>
{
    // Extract incoming token
    var incomingToken = ctx.Request.Headers.Authorization
        .ToString().Replace("Bearer ", "");

    // Exchange for new token (preserves user identity, adds Service A to actor chain)
    var exchangedToken = await tokenClient.ExchangeTokenAsync(
        incomingToken,
        narrowedScopes: new[] { "read:orders" }); // Optional: narrow scopes

    // Use exchanged token to call Service B
    // Service B will see: subject=user, actor=service-a
});
```

### Validating Tokens in Endpoints

```csharp
app.MapGet("/api/orders", [Authorize] async (ClaimsPrincipal user) =>
{
    // Check scopes
    if (!user.HasAllScopes("read:orders"))
        return Results.Forbid();

    // Get original caller identity
    var subject = user.GetOriginalSubject();

    // Check if this is an on-behalf-of request
    if (user.IsOnBehalfOf())
    {
        var immediateActor = user.GetImmediateActor(); // Service that called us
        var fullChain = user.GetActorChainList();      // Full audit trail
    }

    return Results.Ok(await GetOrders(subject));
});
```

### Error Handling

```csharp
try
{
    var token = await tokenClient.GetTokenAsync(cancellationToken: ct);
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
{
    // API key is invalid or revoked - check secret provisioning.
    logger.LogError("Token service rejected this client credential");
    throw;
}
catch (HttpRequestException ex)
{
    // Network, timeout, or non-success HTTP response. Do not log request bodies or credentials.
    logger.LogError("Token service request failed with status {StatusCode}", ex.StatusCode);
    throw;
}
```

## Local verification

Release builds normally consume published sibling packages. To verify all current TokenBroker projects together before those packages are published, use the repository-only project-reference switch:

```powershell
dotnet test --project tests/Ruya.Services.TokenBroker.Unit.Tests/Ruya.Services.TokenBroker.Unit.Tests.csproj `
    --configuration Release `
    -p:UseLocalTokenBrokerProjectReferences=true
```

The test target requires the .NET 8 ASP.NET Core shared runtime. On a machine intentionally carrying only a later runtime, set `DOTNET_ROLL_FORWARD=Major` for a compatibility smoke run; install the .NET 8 runtime for target-runtime verification.

The TokenBroker workflows supply the same property to restore, build, and test so NuGet's static restore graph and the compiler use the same dependency graph. Published packages continue declaring package dependencies on the sibling TokenBroker packages.

## Token Flow

### Basic Token Request

```
Service A                    Token Service
    │                              │
    │ POST /api/v1/token           │
    │ X-Api-Key: <secret>          │
    │ X-Service-Name: service-a    │
    │──────────────────────────────▶
    │                              │
    │       { accessToken: "..." } │
    │◀──────────────────────────────
```

### Token Exchange (On-Behalf-Of) with Chain

```
Service B                    Token Service
    │                              │
    │ POST /api/v1/token/exchange  │
    │ { token: "jwt-with-chain" }  │
    │──────────────────────────────▶
    │                              │
    │ New JWT with updated chain:  │
    │ act: { sub: "service-b",     │
    │        act: { sub: "..." }}  │
    │◀──────────────────────────────
```

## File Structure

```
src/
├── Ruya.Services.TokenBroker.Abstractions/      # Shared by all packages
│   ├── Contracts/
│   │   ├── ITokenBroker.cs
│   │   └── IApiKeyValidator.cs
│   ├── Constants.cs
│   ├── Models.cs                   # TokenRequest, TokenResponse, ActorChain, etc.
│   └── Exceptions.cs
│
├── Ruya.Services.TokenBroker.Validation/        # Lightweight validation only
│   ├── Settings.cs
│   ├── ClaimsPrincipalExtensions.cs
│   └── StartupExtensions.cs        # AddTokenValidation()
│
├── Ruya.Services.TokenBroker.Client/            # HTTP client for requesting tokens
│   ├── Client.cs
│   ├── Settings.cs
│   ├── SettingsValidator.cs
│   └── StartupExtensions.cs        # AddTokenClient()
│
├── Ruya.Services.TokenBroker/                   # Full service (central auth)
│   ├── samples/
│   │   ├── Program.*.cs
│   │   ├── kubernetes-deployment.yaml
│   │   └── SeedApiKeys.cs
│   ├── Settings.cs
│   ├── Service.cs
│   ├── ApiKeyValidator.cs
│   ├── Api.cs                      # MapTokenBrokerApi()
│   ├── HealthCheck.cs
│   └── StartupExtensions.cs        # AddTokenBroker(), AddTokenBrokerWithValidation()
```

## API Keys

API keys are stored as SHA-256 hashes. Registration, removal, and rotation are serialized with a heartbeat-backed `IDistributedLock`; validation also checks the service-name index, so a stale payload cannot authenticate after the active index switches.

As currently implemented, `ApiKeyCacheDuration` is the absolute lifetime of both the registration and its active-key index, not merely an in-process read-cache duration. The provisioning owner must renew each registration before that duration expires.

### Registering and rotating API keys

Always register through `IApiKeyValidator`; writing cache entries directly bypasses the active-key index and atomic rotation contract. Retrieve the plaintext key from a secret provider and pass the caller cancellation token:

```csharp
var apiKey = configuration["TokenBrokerBootstrap:ApiKeys:order-service"]
    ?? throw new InvalidOperationException("The order-service API-key secret is missing.");

await validator.RegisterServiceAsync(
    new ServiceRegistration
    {
        ServiceName = "order-service",
        ApiKeyHash = string.Empty, // The validator computes the stored hash.
        AllowedScopes = ["read:orders", "write:orders"],
        AllowedRoles = ["order-reader"],
        CanExchangeTokens = true
    },
    apiKey,
    cancellationToken);
```

Calling the same method with a new key rotates the credential. The new registration is written first, the active index switches under the distributed lock, and the old payload is then removed. If the index write reports an uncertain outcome, cleanup first verifies which hash is active and never removes a payload referenced by the active index. Re-registering the same key refreshes its payload without destructive compensation, so a failed index refresh does not invalidate the current credential. Provisioning calls remain safe to retry. Never log or return the plaintext key after the one controlled provisioning handoff.

## Signing-key rotation

Rotate signing keys with an overlap window:

1. Add the new public key to `SigningPublicKeys` on the broker and every validator while retaining the previous public key.
2. Deploy validators and the broker with the overlapping ring.
3. Switch `SigningKeyId` and `SigningPrivateKeyPem` to the new matching pair. New tokens carry the new `kid`; broker validation and exchange continue accepting unexpired tokens carrying the previous `kid`.
4. Remove the previous public key only after its last possible token lifetime plus clock skew has elapsed.

The private key remains issuer-only throughout the rotation.

## Extensions

```csharp
// Check scopes
if (!user.HasAllScopes("read:orders")) return Forbid();

// Get original caller (user or initiating service)
var originalCaller = user.GetOriginalSubject();

// Get full actor chain
var chain = user.GetActorChainList(); // ["service-n", "service2", "service1"]

// Get immediate actor (who called us directly)
var caller = user.GetImmediateActor(); // "service-n"

// Get original actor (first service in chain)
var firstService = user.GetOriginalActor(); // "service1"

// Check if token was exchanged
if (user.IsOnBehalfOf()) { }

// Check if specific service is in chain
if (user.HasActorInChain("service2")) { }
```

## Observability

Built-in metrics:
- `token_service_tokens_created_total`
- `token_service_tokens_exchanged_total`
- `token_service_token_validation_failures_total`
- `token_service_token_creation_duration_seconds`
- `token_service_api_key_validations_total`
- `token_service_api_key_validation_failures_total`

## Boundary with user identity providers

TokenBroker is an issuer and validator for its own service-to-service tokens. Its exchange endpoint accepts only a still-valid TokenBroker token signed by one of the configured broker keys. It does **not** accept or translate an arbitrary OAuth/OIDC token from an external identity provider.

An API gateway that accepts user tokens must validate those tokens with the identity provider's dedicated authentication scheme. If the gateway also calls internal services, keep that trust boundary explicit: request a broker service token for the gateway's registered service identity, or implement a separately reviewed federation/delegation component. Do not pass an external user token to `ExchangeTokenAsync` and assume it will be trusted or preserve the user identity.

`AddTokenValidation()` configures the default JWT bearer scheme for TokenBroker tokens. Applications with multiple issuers should configure named schemes and authorization policies deliberately rather than registering competing defaults.

## Security Notes

1. Keep the issuer internal (`ClusterIP` or an equivalent private boundary) and require TLS in transit.
2. Store the RSA private key, Redis connection string, and client API keys only in a secret provider. Validators receive public keys only.
3. Pin validation to RSA-SHA256 and rotate with overlapping `SigningPublicKeys` entries and distinct `kid` values.
4. API keys are SHA-256 hashed and rotations are serialized with a distributed lock; never seed the cache directly.
5. Tokens are short-lived (15 minutes by default), and exchanged tokens never outlive the original token.
6. Token exchange cannot elevate the original scopes or exceed the actor service's registered scopes.
7. Roles are issuer-owned and must be listed in the service registration; reserved claims cannot be overridden.
8. `/api/v1/token/validate` requires the same API-key/service-name authentication as issuance. The `/api/token` prefix is a secured, deprecated 8.x compatibility alias.
9. Logs and metric tags intentionally omit subjects, JWT IDs, tokens, API keys, and actor-chain contents.
