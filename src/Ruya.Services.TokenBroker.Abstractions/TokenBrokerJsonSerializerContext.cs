using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(TokenExchangeRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(ActorChain))]
[JsonSerializable(typeof(ServiceRegistration))]
[JsonSerializable(typeof(CreateTokenApiRequest))]
[JsonSerializable(typeof(ExchangeTokenApiRequest))]
[JsonSerializable(typeof(ValidateTokenApiRequest))]
[JsonSerializable(typeof(TokenValidationResult))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(JsonElement))]
public partial class TokenBrokerJsonSerializerContext : JsonSerializerContext;
