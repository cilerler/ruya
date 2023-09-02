using System.Collections.Generic;

namespace Ruya.Bus.RabbitMQ;

public class Queue
{
	public string Name { set; get; }
	public bool Durable { set; get; } = true;
	public bool AutoDelete { set; get; } = false;
	public bool Exclusive { set; get; } = false;

	public Dictionary<string, object> Arguments { set; get; }
	//= new Dictionary<string, object>
	//{
	//	{"x-message-ttl", 600000 }, // do not use ttl it will throw an error as explained in here https://groups.google.com/forum/#!topic/rabbitmq-users/zxNhgFa4glI/discussion
	//	{"x-dead-letter-exchange", "myExchange.DLX"},
	//	{"x-dead-letter-routing-key", "myQueue.DLK" },
	//};
}
