namespace Ruya.Bus.RabbitMQ;

public static class DeadLetterHelper
{
	public const string DeadLetterExchangeType = "topic";
	private const string DeadLetterQueueSuffix = ".Errors";
	private const string Exchange = "x-dead-letter-exchange";
	private const string RoutingKey = "x-dead-letter-routing-key";

	private static (string Value, bool ValueExists) GetValue(string key, Queue queue)
	{
		string value = string.Empty;
		if (queue.Arguments == null) return ( value, false );

		bool keyExists = queue.Arguments.ContainsKey(key);
		if (keyExists) value = queue.Arguments[key] as string;

		bool valueExists = !string.IsNullOrWhiteSpace(value);
		return ( value, valueExists );
	}

	public static (string DeadLetterExchange, string DeadLetterRoutingKey, string DeadLetterQueue, bool DeadLetterExists) GetValues(Queue queue)
	{
		string dlq = queue.Name + DeadLetterQueueSuffix;
		( string dlx, bool dlxExists ) = GetValue(Exchange, queue);
		( string dlk, bool dlkExists ) = GetValue(RoutingKey, queue);
		bool dlExists = dlxExists && dlkExists;
		return ( dlx, dlk, dlq, dlExists );
	}
}
