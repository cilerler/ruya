namespace Ruya.Testing.Primitives.SampleTest;

internal interface IPingPongService
{
	string Ping(string message);
}

internal class PingPongService : IPingPongService
{
	public string Ping(string message)
	{
		return $"Pong: {message}";
	}
}
