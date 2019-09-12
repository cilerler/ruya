using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Ruya.Core;
using Ruya.Diagnostics;

namespace Ruya.Connector.Interfaces
{
    public static class Constants
    {
#warning Refactor
        public const string ServerMemoryMappedFileName = @"Global\RuyaHostAddress";
        public const string HubName = "defaultHub";
        public const string WordEqualToAdministrator = "Observer";
        public const string WordEqualToClientName = "Instance";
        public const string WordEqualToGroups = "Hubs";
        public const string WordEqualToNotifyGroups = "notifyGroups";
        public const string WordEqualToTriggerGroups = "triggerGroups";
        public const string WordEqualToNotifyAdministrators = "notifyObservers";
        public const string WordEqualToTrigger = "trigger";
    }
}

namespace Ruya.Host
{
    [HubName(Connector.Interfaces.Constants.HubName)]
    public class DefaultHub : Hub
    {
        private static readonly Dictionary<string, string> ClientList = new Dictionary<string, string>();
        private static readonly List<KeyValuePair<string, string>> GroupList = new List<KeyValuePair<string, string>>();
        

        public override Task OnConnected()
        {
            AddClient();
            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            RemoveClient(stopCalled);
            return base.OnDisconnected(stopCalled);
        }

        public override Task OnReconnected()
        {
            if (!ClientList.ContainsKey(Context.ConnectionId))
            {
                AddClient();
            }
            else
            {
                KeyValuePair<string, string> source = GetSource();

                // HARD-CODED constant
                string traceMessage = $"{source.Value} ({Context.ConnectionId}) is reconnected.";
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, traceMessage);
                NotifyObservers(traceMessage);
            }
            return base.OnReconnected();
        }
        
        private KeyValuePair<string, string> GetSource()
        {
            string sourceId = Context.ConnectionId;
            string source = ClientList[sourceId];
            return new KeyValuePair<string, string>(sourceId, source);
        }

        private void AddClient()
        {
            string clientName = Context.Headers[Connector.Interfaces.Constants.WordEqualToClientName];
            bool client = !string.IsNullOrEmpty(clientName);
            if (!client)
            {
                clientName = Context.QueryString[Connector.Interfaces.Constants.WordEqualToClientName];                
            }
            bool isListener = !client && clientName.Equals(Connector.Interfaces.Constants.WordEqualToAdministrator);

            bool validClient = client || isListener;
            if (!validClient)
            {
                // HARD-CODED constant
                const string traceMessage = "There is a client which shouldn't be here, setting it as listener!";
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, traceMessage);
                NotifyObservers(traceMessage);
                return;
            }

            ClientList.Add(Context.ConnectionId, clientName);
            KeyValuePair<string, string> source = GetSource();


            // HARD-CODED constant
            string message = $"{source.Value} ({source.Key}) added as a new client.";
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, message);
            NotifyObservers(message);


            if (isListener)
            {
                JoinGroup(Connector.Interfaces.Constants.WordEqualToAdministrator).Wait();
            }
            else
            {
                string groups = Context.Headers[Connector.Interfaces.Constants.WordEqualToGroups];
                if (string.IsNullOrWhiteSpace(groups))
                {
                    // HARD-CODED constant
                    string traceMessage = $"There is no groups registered for the client {source.Value} ({source.Key})";
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, traceMessage);
                    NotifyObservers(traceMessage);
                }
                else
                {
                    foreach (string group in groups.Split(ControlChars.Comma))
                    {
                        JoinGroup(group).Wait();
                    }   
                }
            }
        }

        private void CustomClientAllExcept(string methodToCall, string[] exceptList, string message)
        {
            //x const string methodToCall = "notify";
            //x string[] exceptList = {};
            IClientProxy clients = Clients.AllExcept(exceptList);
            clients.Invoke(methodToCall, message);
        }

        private void RemoveClient(bool stopCalled)
        {
            KeyValuePair<string, string> source = GetSource();

            IEnumerable<string> localCopyOfMembers = GroupList.Where(gl => gl.Key == source.Key).Select(gl=>gl.Value).ToList();
            foreach (string groupName in localCopyOfMembers)
            {
                LeaveGroup(groupName, stopCalled).Wait();
            }

            ClientList.Remove(source.Key);

            // HARD-CODED constant
            string stopType = stopCalled
                                  ? "EXPLICITLY"
                                  : "TIMEOUT";
            string traceMessage = $"{source.Value} ({source.Key}) removed from the client list.  Stop type is {stopType}";
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, traceMessage);
            NotifyObservers(traceMessage);
        }
        
        public async Task JoinGroup(string groupName)
        {
            KeyValuePair<string, string> source = GetSource();

            await Groups.Add(source.Key, groupName);
            GroupList.Add(new KeyValuePair<string, string>(source.Key, groupName));
            
            // HARD-CODED constant
            string traceMessage = $"{source.Value} ({source.Key}) added into group {groupName}.";
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, traceMessage);
            Clients.Group(groupName).notify(traceMessage);

            if (!groupName.Equals(Connector.Interfaces.Constants.WordEqualToAdministrator))
            {
                NotifyObservers(traceMessage);
            }
        }

        public async Task LeaveGroup(string groupName, bool stopCalled)
        {
            KeyValuePair<string, string> source = GetSource();

            var groupItem = new KeyValuePair<string, string>(source.Key, groupName);
            bool groupAvailable = GroupList.Any(gl => gl.Equals(groupItem));
            if (!groupAvailable)
            {
                Clients.Caller.notify("Rejected, there is no group");
                return;
            }            
            GroupList.Remove(groupItem);

            if (!stopCalled) await Groups.Remove(Context.ConnectionId, groupName);

            // HARD-CODED constant
            string traceMessage = $"{source.Value} ({source.Key}) removed from the group {groupName}";
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, traceMessage);
            Clients.Group(groupName).notify(traceMessage);

            if (!groupName.Equals(Connector.Interfaces.Constants.WordEqualToAdministrator))
            {
                NotifyObservers(traceMessage);
            }
        }

        public bool IsConnected()
        {
            return ClientList.Any(cl => cl.Key.Equals(Context.ConnectionId));
        }

        public IEnumerable<string> GetClientNames()
        {
                return ClientList.Keys.Distinct();
        }

        public IEnumerable<string> GetGroupNames()
        {
            return GroupList.Select(gl => gl.Value)
                            .Distinct();
        }
        
        public void Notify(string message)
        {
            Tracer.Instance.TraceData(TraceEventType.Verbose, 0, MethodBase.GetCurrentMethod().GetQualifiedName(), message);
            Clients.All.notify(message);            
        }

        public async void NotifyGroups(string[] groups, string message, bool console)
        {
            Tracer.Instance.TraceData(TraceEventType.Verbose, 0, MethodBase.GetCurrentMethod()
                                                                           .GetQualifiedName(), message);
            if (console)
            {

                await Clients.OthersInGroups(groups)
                             .notifyConsole(message);

            }
            else
            {
                await Clients.OthersInGroups(groups)
                             .notify(message);
            }
        }

        public async void TriggerGroups(string[] groups, string message)
        {
            Tracer.Instance.TraceData(TraceEventType.Verbose, 0, MethodBase.GetCurrentMethod().GetQualifiedName(), message);
            foreach (string group in groups)
            {
                await Clients.OthersInGroup(group)
                             .trigger(group, message);                
            }
        }
        
        public void NotifyObservers(string message)
        {
            NotifyGroups(new[]
                         {
                             Connector.Interfaces.Constants.WordEqualToAdministrator
                         }, message, false);
        }
    }
}
