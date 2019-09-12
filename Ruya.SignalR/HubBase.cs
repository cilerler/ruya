using System;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;

namespace Ruya.SignalR
{
    public abstract class HubBase<T> where T : IHub
    {
        private readonly Lazy<IHubContext> _hub = new Lazy<IHubContext>(() => GlobalHost.ConnectionManager.GetHubContext<T>());

        public IHubContext Hub => _hub.Value;
    }
}