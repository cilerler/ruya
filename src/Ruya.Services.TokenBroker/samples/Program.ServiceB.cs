// Program.cs for Service B (receives token from A, exchanges for calling C)

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Ruya.Services.TokenBroker;
using Ruya.Services.TokenBroker.Client;
using Ruya.Services.TokenBroker.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Token Client (to exchange tokens)
builder.Services.AddTokenClient();

// Add Token Validation (to validate incoming tokens)
builder.Services.AddTokenValidation();

// Add HttpClient for calling Service C
builder.Services.AddHttpClient("ServiceC", client =>
{
    client.BaseAddress = new Uri("https://service-c");
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Example: Receive request from Service A, call Service C on behalf of A
app.MapGet("/api/orders", [Authorize] async (
    HttpContext httpContext,
    ITokenClient tokenClient,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    var user = httpContext.User;

    var actorChain = user.GetActorChainList();

    // Check scopes
    if (!user.HasAllScopes("read:orders"))
    {
        return Results.Forbid();
    }

    // Get the original token from the Authorization header
    var originalToken = httpContext.Request.Headers.Authorization
        .ToString()
        .Replace("Bearer ", "");

    // Exchange token to call Service C (narrowing scope if needed)
    var exchangedToken = await tokenClient.ExchangeTokenAsync(
        originalToken,
        ["read:inventory"], // Narrower scope for Service C
        cancellationToken);

    // Call Service C with the exchanged token
    var client = httpClientFactory.CreateClient("ServiceC");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", exchangedToken);

    using var response = await client.GetAsync("/api/inventory", cancellationToken);
    response.EnsureSuccessStatusCode();

    var inventoryData = await response.Content.ReadAsStringAsync(cancellationToken);

    return Results.Ok(new
    {
        ActorDepth = actorChain.Count,
        InventoryData = inventoryData
    });
})
.RequireAuthorization();

app.Run();
