using System;

namespace Ruya.Services.MessageQueue.RabbitMq
{
    public class MessageQueueEventArgs : EventArgs
    {
        public string Message { set; get; }
        public ulong DeliveryTag { get; set; }
    }
}
