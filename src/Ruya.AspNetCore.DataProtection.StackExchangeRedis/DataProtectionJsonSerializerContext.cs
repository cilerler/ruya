using System.Text.Json.Serialization;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DataProtectionSettings))]
internal sealed partial class DataProtectionJsonSerializerContext : JsonSerializerContext;
