// Program.cs for Service C (final destination, validates exchanged tokens)

using Microsoft.AspNetCore.Authorization;
using Ruya.Services.TokenBroker;
using Ruya.Services.TokenBroker.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Only needs token validation (doesn't issue or exchange tokens)
builder.Services.AddTokenValidation();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Service C can see the full chain: original caller + all actors
app.MapGet("/api/inventory", [Authorize] (HttpContext httpContext) =>
{
    var user = httpContext.User;

    var originalSubject = user.GetOriginalSubject(); // service-a (original requester in this sample)
    var actorChain = user.GetActorChainList();       // [service-b]
    var immediateActor = user.GetImmediateActor();   // service-b (who exchanged the token)
    var originalActor = user.GetOriginalActor();     // service-b (first actor after the subject)
    var scopes = user.GetScopes();

    // Verify required scope
    if (!user.HasAllScopes("read:inventory"))
    {
        return Results.Forbid();
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
