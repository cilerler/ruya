namespace Ruya.Services.MessageQueue.InMemory.Integration.Tests;

internal sealed class TestMessage
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;
}
