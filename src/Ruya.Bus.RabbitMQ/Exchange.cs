using System.Collections.Generic;

namespace Ruya.Bus.RabbitMQ;

public class Exchange
{
	public string Name { set; get; }
	public string Type { set; get; }
	public bool Durable { set; get; } = true;
	public bool AutoDelete { set; get; } = false;
	public Dictionary<string, object> Arguments { set; get; }
}
