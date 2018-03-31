using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ruya.Helpers.Primitives
{
    public sealed class RetryWithExponentialBackoff
    {
        private readonly ILogger _logger;
        private readonly TimeSpan _delay, _maxDelay;
        private readonly int _maxRetries;

        public RetryWithExponentialBackoff(ILogger logger, int maxRetries = 5, TimeSpan? delay = null, TimeSpan? maxDelay = null)
        {
            if (delay == null) delay = TimeSpan.FromSeconds(1);

            if (maxDelay == null) maxDelay = TimeSpan.FromSeconds(10);

            _logger = logger;
            _maxRetries = maxRetries;
            _delay = (TimeSpan)delay;
            _maxDelay = (TimeSpan)maxDelay;
        }

        public async Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default(CancellationToken))
        {
            var backoff = new ExponentialBackoff(_logger, _maxRetries, _delay, _maxDelay);
            bool retry;
            do
            {
                try
                {
                    await func(cancellationToken);
                    retry = false;
                }
                catch (Exception ex) when (ex is TimeoutException || ex is HttpRequestException)
                {
                    _logger.LogWarning(ex, ex.Message);
                    try
                    {
                        await backoff.Delay(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        retry = true;
                    }
                    catch (OperationCanceledException oce)
                    {
                        _logger.LogInformation($"Cancellation requested {oce.Message}");
                        retry = false;
                    }
                }
            }
            while (retry);
        }
    }
}
