using System;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ruya.Services.TokenBroker.Client;

internal sealed class TokenClientSettingsValidator(IHostEnvironment? hostEnvironment = null)
    : IValidateOptions<TokenClientSettings>
{
    public ValidateOptionsResult Validate(string? name, TokenClientSettings options)
    {
        if (!Uri.TryCreate(options.TokenBrokerUrl, UriKind.Absolute, out var uri))
        {
            return ValidateOptionsResult.Fail("TokenClient:TokenBrokerUrl must be an absolute URI.");
        }

        var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !isHttp)
        {
            return ValidateOptionsResult.Fail("TokenClient:TokenBrokerUrl must use HTTP or HTTPS.");
        }

        if (options.AllowInsecureHttpForDevelopment
            && !(hostEnvironment?.IsDevelopment() ?? false))
        {
            return ValidateOptionsResult.Fail(
                "TokenClient:AllowInsecureHttpForDevelopment may be enabled only when the host environment is Development.");
        }

        if (!isHttps
            && !uri.IsLoopback
            && !options.AllowInsecureHttpForDevelopment)
        {
            return ValidateOptionsResult.Fail(
                "TokenClient:TokenBrokerUrl must use HTTPS outside loopback. "
                + "AllowInsecureHttpForDevelopment may be enabled only in an explicit development configuration.");
        }

        return ValidateOptionsResult.Success;
    }
}
