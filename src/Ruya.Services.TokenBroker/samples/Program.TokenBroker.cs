// Program.cs for Token Service (the issuer)

using Ruya.Services.TokenBroker;
using Ruya.Services.DistributedLock.Redis.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is required from the active secret provider.");

// Add distributed cache (Redis for production)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
});
builder.Services.AddRedisDistributedLock();

// Add Token Service
builder.Services.AddTokenBroker();

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapHealthChecks("/healthz/startup", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("startup")
});
app.MapTokenBrokerApi();

app.Run();
