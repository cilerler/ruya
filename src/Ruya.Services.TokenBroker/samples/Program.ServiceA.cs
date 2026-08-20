// Program.cs for Service A (requests tokens, calls Service B)

using System.Net.Http.Headers;
using Ruya.Services.TokenBroker;
using Ruya.Services.TokenBroker.Client;

var builder = WebApplication.CreateBuilder(args);

// Add Token Client (to request tokens from Token Service)
builder.Services.AddTokenClient();

// Add Token Validation (to validate incoming tokens)
builder.Services.AddTokenValidation();

// Add HttpClient for calling Service B
builder.Services.AddHttpClient("ServiceB", client =>
{
    client.BaseAddress = new Uri("https://service-b");
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Example: Call Service B with a fresh token
app.MapGet("/call-service-b", async (
    ITokenClient tokenClient,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    // Get a token for calling Service B
    var token = await tokenClient.GetTokenAsync(
        ["read:orders", "read:inventory"],
        cancellationToken: cancellationToken);

    // Call Service B
    var client = httpClientFactory.CreateClient("ServiceB");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    using var response = await client.GetAsync("/api/orders", cancellationToken);
    response.EnsureSuccessStatusCode();

    return await response.Content.ReadAsStringAsync(cancellationToken);
});

app.Run();
