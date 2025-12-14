using System;
using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.TokenBroker.Client;

public class TokenClientSettings
{
    public const string ConfigurationSectionName = "TokenClient";

    [Required]
    [Url]
    public required string TokenBrokerUrl { get; set; }

    [Required]
    [MinLength(1)]
    public required string ServiceName { get; set; }

    [Required]
    [MinLength(16)]
    public required string ApiKey { get; set; }

    [Range(typeof(TimeSpan), "00:00:30", "00:10:00")]
    public TimeSpan TokenRefreshBuffer { get; set; } = TimeSpan.FromMinutes(1);
}
