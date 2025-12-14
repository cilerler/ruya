// Program.cs for Token Service (the issuer)

using Ruya.Services.TokenBroker;

var builder = WebApplication.CreateBuilder(args);

// Add distributed cache (Redis for production)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

// Add Token Service
builder.Services.AddTokenBroker(builder.Configuration);

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<TokenBrokerHealthCheck>("token-service");

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapTokenBrokerApi();

app.Run();
