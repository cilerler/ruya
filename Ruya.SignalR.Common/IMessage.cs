namespace Ruya.SignalR.Common
{
    public interface IMessage
    {
        Command Command { get; set; }
        string Content { get; set; }
        string Target { get; set; }
    }
}