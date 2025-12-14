// Program.cs for Service C (final destination, validates exchanged tokens)

using Microsoft.AspNetCore.Authorization;
using Ruya.Services.TokenBroker;
using Ruya.Services.TokenBroker.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Only needs token validation (doesn't issue or exchange tokens)
builder.Services.AddTokenValidation(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Service C can see the full chain: original caller + all actors
app.MapGet("/api/inventory", [Authorize] (HttpContext httpContext) =>
{
    var user = httpContext.User;

    var originalSubject = user.GetOriginalSubject(); // user-123 or service-a (original requester)
    var actorChain = user.GetActorChainList();       // [service-c, service-b, service-a] full chain
    var immediateActor = user.GetImmediateActor();   // service-c (who called us)
    var originalActor = user.GetOriginalActor();     // service-a (first service after subject)
    var scopes = user.GetScopes();

    // Verify required scope
    if (!user.HasAllScopes("read:inventory"))
    {
        return Results.Forbid();
    }

    // Log full request chain
    if (actorChain.Count > 0)
    {
        Console.WriteLine($"Inventory request: {originalSubject} → {string.Join(" → ", actorChain.Reverse())} → Service-C");
    }
    else
    {
        Console.WriteLine($"Direct inventory request from: {originalSubject}");
    }

    return Results.Ok(new
    {
        OriginalCaller = originalSubject,
        ActorChain = actorChain,
        ImmediateActor = immediateActor,
        OriginalActor = originalActor,
        Scopes = scopes,
        Data = new[]
        {
            new { Sku = "ABC-123", Quantity = 100 },
            new { Sku = "XYZ-789", Quantity = 50 }
        }
    });
})
.RequireAuthorization();

app.Run();
