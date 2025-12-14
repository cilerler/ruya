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
User → API → Service1 → Service2 → Service-N
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

**Scenario 1: User request through services**
- `GetOriginalSubject()` → "user-123"
- `GetActorChainList()` → ["service-n", "service2", "service1"]
- `GetImmediateActor()` → "service-n" (who called us)
- `GetOriginalActor()` → "service1" (first service after user)

**Scenario 2: Cronjob/background job**
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
| **Ruya.Services.TokenBroker** | Full service: issuing, validation, Redis API keys | Ruya.Services.TokenBroker.Abstractions, Ruya.Services.TokenBroker.Validation |

## Quick Start

### 1. Generate a signing key

```bash
openssl rand -base64 32
```

### 2. Configure appsettings.json

```json
{
  "TokenBroker": {
    "Issuer": "ruya-token-service",
    "Audiences": [
      "ruya-api",
      "ruya-services"
    ],
    "SigningKeyBase64": "YOUR_BASE64_ENCODED_256_BIT_KEY_HERE",
    "TokenLifetime": "00:15:00",
    "ClockSkew": "00:00:30",
    "ApiKeysCacheKey": "token-service:valid-api-keys",
    "ApiKeyCacheDuration": "00:05:00"
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}

```

### 3. Register in Program.cs

**Token Service (issuer):**
```csharp
using Ruya.Services.TokenBroker;

builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
builder.Services.AddTokenBroker();
app.MapTokenBrokerApi();
```

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
  "TokenBroker": {
    "Issuer": "ruya-token-service",
    "Audiences": [
      "ruya-api",
      "ruya-services"
    ],
    "SigningKeyBase64": "YOUR_BASE64_ENCODED_256_BIT_KEY_HERE",
    "ClockSkew": "00:00:30"
  },
  "TokenClient": {
    "TokenBrokerUrl": "http://token-service:8080",
    "ServiceName": "service-a",
    "ApiKey": "YOUR_SERVICE_A_API_KEY_HERE",
    "TokenRefreshBuffer": "00:01:00"
  }
}
```

## Resilience & Fault Tolerance

The `TokenClient` includes built-in resilience using .NET 8's `Microsoft.Extensions.Http.Resilience`:

- **Retry**: 3 attempts with exponential backoff (1s, 2s, 4s)
- **Circuit Breaker**: Opens after 5 consecutive failures, stays open for 30 seconds
- **Timeout**: 30 second per-request timeout

This ensures your services gracefully handle Token Service outages:

```
Token Service goes down for 5 minutes:
├── Services with valid cached tokens → Continue working normally
├── Services needing refresh → Retry with backoff, then fail gracefully
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
    // Customize retry
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
    if (!user.HasScope("read:orders"))
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
catch (TokenBrokerException ex) when (ex is InvalidApiKeyException)
{
    // API key is invalid or revoked - check configuration
    logger.LogError(ex, "Invalid API key for service");
    throw;
}
catch (TokenBrokerException ex)
{
    // General token service error - retry or alert
    logger.LogError(ex, "Token service unavailable");
    throw;
}
catch (HttpRequestException ex)
{
    // Network error to token service
    logger.LogError(ex, "Cannot reach token service");
    throw;
}
```

## Token Flow

### Basic Token Request

```
Service A                    Token Service
    │                              │
    │ POST /api/token              │
    │ X-Api-Key: abc123            │
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
    │ POST /api/token/exchange     │
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
│   ├── TokenValidationSettings.cs
│   ├── ClaimsPrincipalExtensions.cs
│   └── StartupExtensions.cs        # AddTokenValidation()
│
├── Ruya.Services.TokenBroker.Client/            # HTTP client for requesting tokens
│   ├── TokenClient.cs
│   ├── TokenClientSettings.cs
│   └── StartupExtensions.cs        # AddTokenClient()
│
├── Ruya.Services.TokenBroker/                   # Full service (central auth)
│   ├── examples/
│   │   ├── Program.*.cs
│   │   ├── appsettings.*.json
│   │   ├── kubernetes-deployment.yaml
│   │   └── SeedApiKeys.cs
│   ├── TokenBrokerSettings.cs
│   ├── TokenBroker.cs
│   ├── ApiKeyValidator.cs
│   ├── Api.cs                      # MapTokenBrokerApi()
│   ├── HealthCheck.cs
│   └── StartupExtensions.cs        # AddTokenBroker(), AddTokenBrokerWithValidation()
```

## API Keys

API keys are stored in Redis as SHA256 hashed values.

### Seeding API Keys

**Option 1: Startup seeding (development/testing)**

```csharp
// In Program.cs of your Token Service
var apiKeyValidator = app.Services.GetRequiredService<IApiKeyValidator>();

await apiKeyValidator.RegisterServiceAsync(
    new ServiceRegistration
    {
        ServiceName = "order-service",
        ApiKeyHash = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes("dev-api-key-12345"))),
        AllowedScopes = new[] { "read:orders", "write:orders" },
        CanExchangeTokens = true
    },
    "dev-api-key-12345",  // Plain key for index lookup
    CancellationToken.None);
```

**Option 2: Admin API endpoint**

```csharp
// Secure admin endpoint (protect appropriately!)
app.MapPost("/admin/api-keys", [Authorize(Roles = "admin")]
    async (RegisterServiceRequest request, IApiKeyValidator validator) =>
{
    var apiKey = GenerateSecureApiKey(); // Generate secure random key
    await validator.RegisterServiceAsync(
        new ServiceRegistration
        {
            ServiceName = request.ServiceName,
            ApiKeyHash = Convert.ToBase64String(
                SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))),
            AllowedScopes = request.Scopes,
            CanExchangeTokens = request.CanExchange
        },
        apiKey,
        CancellationToken.None);

    return Results.Ok(new { ApiKey = apiKey }); // Return key ONCE
});
```

**Option 3: Migration/seed script**

```csharp
// Separate console app or migration
public class SeedApiKeys
{
    public static async Task SeedAsync(IApiKeyValidator validator)
    {
        var services = new[]
        {
            ("order-service", "read:orders,write:orders", true),
            ("inventory-service", "read:inventory", false),
            ("reporting-service", "read:*", false)
        };

        foreach (var (name, scopes, canExchange) in services)
        {
            var apiKey = Environment.GetEnvironmentVariable($"{name.ToUpper()}_API_KEY")
                ?? throw new InvalidOperationException($"Missing API key for {name}");

            await validator.RegisterServiceAsync(
                new ServiceRegistration
                {
                    ServiceName = name,
                    ApiKeyHash = Convert.ToBase64String(
                        SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))),
                    AllowedScopes = scopes.Split(','),
                    CanExchangeTokens = canExchange
                },
                apiKey,
                CancellationToken.None);
        }
    }
}
```

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

## Integration with ASP.NET Identity

This library handles **service-to-service (S2S)** authentication, which is separate from **user authentication**. They work together:

```
┌──────────────────────────────────────────────────────────────────────┐
│                        User Authentication                            │
│   (ASP.NET Identity, OAuth2, OpenID Connect, IdentityServer, etc.)   │
│                                                                       │
│   User logs in → Gets user JWT → Calls your API Gateway              │
└───────────────────────────────────┬──────────────────────────────────┘
                                    │
                                    ▼
┌──────────────────────────────────────────────────────────────────────┐
│                    Service-to-Service Authentication                  │
│                    (This Token Service library)                       │
│                                                                       │
│   API Gateway → Token Exchange → Internal Services                   │
│   (preserves user identity in 'sub', adds services to 'act' chain)   │
└──────────────────────────────────────────────────────────────────────┘
```

### Example: API Gateway with User Tokens

```csharp
// API Gateway receives user token from Identity Provider
app.MapGet("/api/orders", [Authorize] async (
    HttpContext ctx,
    ITokenClient tokenClient,
    HttpClient orderServiceClient) =>
{
    // Extract the incoming user token
    var incomingToken = ctx.Request.Headers.Authorization
        .ToString().Replace("Bearer ", "");

    // Exchange the user token for an S2S token (preserves user identity, adds gateway to actor chain)
    var exchangedToken = await tokenClient.ExchangeTokenAsync(
        incomingToken,
        narrowedScopes: ["read:orders"]);

    // Use exchanged token to call internal service
    // Internal service will see: subject=user, actor=api-gateway
    orderServiceClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", exchangedToken);

    return await orderServiceClient.GetFromJsonAsync<Order[]>("/orders");
});
```

### Combined Authentication Setup

```csharp
// In a microservice that receives both user and S2S tokens
builder.Services
    .AddAuthentication()
    .AddJwtBearer("Users", options => { /* User token validation */ })
    .AddJwtBearer("Services", options => { /* S2S token validation from TokenBroker */ });

// Use AddTokenValidation for S2S tokens
builder.Services.AddTokenValidation();

// Policy-based authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ServiceOnly", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes("Services"));

    options.AddPolicy("UserOrService", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes("Users", "Services"));
});
```

## Security Notes

1. Token Service should be **ClusterIP only** (no external access)
2. API keys are SHA256 hashed before storage
3. Tokens are short-lived (15 min default)
4. Token exchange cannot elevate scopes
5. Services can be restricted from token exchange via `CanExchangeTokens`
6. Full actor chain provides audit trail for requests
