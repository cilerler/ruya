using System;

using Microsoft.Extensions.Options;

using Ruya.Services.DistributedLock.Abstractions;

namespace Ruya.Services.TokenBroker;

internal sealed class TokenBrokerDependencyValidator : IValidateOptions<TokenBrokerSettings>
{
    public TokenBrokerDependencyValidator(IDistributedLock distributedLock)
    {
        ArgumentNullException.ThrowIfNull(distributedLock);
    }

    public ValidateOptionsResult Validate(string? name, TokenBrokerSettings options) =>
        ValidateOptionsResult.Success;
}
