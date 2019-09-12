namespace Ruya.SignalR.Common
{
    public class Message : IMessage
    {
        public Command Command { get; set; }

        public string Target { get; set; }

        public string Content { get; set; }
    }
}