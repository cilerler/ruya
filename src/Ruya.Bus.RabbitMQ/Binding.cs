namespace Ruya.Bus.RabbitMQ;

public class Binding
{
	public string Source { set; get; }
	public string Destination { set; get; }
	public string RoutingKey { set; get; }
}
