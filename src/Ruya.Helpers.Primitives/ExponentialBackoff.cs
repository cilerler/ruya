using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ruya.Helpers.Primitives;

public struct ExponentialBackoff
{
	private readonly ILogger _logger;
	private readonly int _maxRetries;
	private readonly TimeSpan _delay, _maxDelay;
	private int _retries;
	private double _pow;

	public ExponentialBackoff(ILogger logger, int maxRetries, TimeSpan delay, TimeSpan maxDelay)
	{
		_logger = logger;
		_maxRetries = maxRetries;
		_delay = delay;
		_maxDelay = maxDelay;
		_retries = 0;
		_pow = 1;
	}

	public Task Delay(CancellationToken cancellationToken = default)
	{
		if (_retries == _maxRetries)
			throw new TimeoutException("Max retry attempts exceeded.");

		++_retries;
		if (_retries < 31)
			_pow = Math.Pow(2, _retries - 1); //x _mPow = _mPow << 1;

		var delay = Convert.ToInt32(Math.Truncate(Math.Min(_delay.TotalMilliseconds * ( _pow - 1 ) / 2, _maxDelay.TotalMilliseconds)));

		_logger.LogDebug("Wait {delay} milliseconds before retry. Retry {retry}/{maxRetry}", delay, _retries, _maxRetries);
		return Task.Delay(delay, cancellationToken);
	}
}
