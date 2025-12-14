using System;

namespace Ruya.Services.TokenBroker.Exceptions;

public class TokenBrokerException : Exception
{
    public string ErrorCode { get; }

    public TokenBrokerException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public TokenBrokerException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

public class InvalidApiKeyException : TokenBrokerException
{
    public InvalidApiKeyException()
        : base("The provided API key is invalid.", Constants.Errors.InvalidApiKey)
    {
    }
}

public class InvalidTokenException : TokenBrokerException
{
    public InvalidTokenException(string reason)
        : base($"The provided token is invalid: {reason}", Constants.Errors.InvalidToken)
    {
    }

    public InvalidTokenException(string reason, Exception innerException)
        : base($"The provided token is invalid: {reason}", Constants.Errors.InvalidToken, innerException)
    {
    }
}

public class TokenExchangeNotAllowedException : TokenBrokerException
{
    public TokenExchangeNotAllowedException(string serviceName)
        : base($"Service '{serviceName}' is not allowed to perform token exchange.", Constants.Errors.ExchangeNotAllowed)
    {
    }
}
