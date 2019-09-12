using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Ruya.SignalR.Common;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;

namespace Ruya.SignalR.Server
{
    [HubName("HelloWorldHub")]
    public class MyHub : Hub
    {

        internal static readonly Dictionary<string, string> ClientList = new Dictionary<string, string>();
        internal static readonly List<KeyValuePair<string, string>> GroupList = new List<KeyValuePair<string, string>>();
        
        public override Task OnConnected()
        {
            ClientList.Add(Context.ConnectionId, null);

            Console.WriteLine("[{0}] {1}\n\r{2}\r\n{3}\r\n=====", MethodBase.GetCurrentMethod().Name, Context.ConnectionId, Context.Headers["clientName"], Context.QueryString["version"]);
            WriteNumberOfConnections();

            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            GroupList.RemoveAll(gl=>gl.Key==Context.ConnectionId);
            ClientList.Remove(Context.ConnectionId);            
            Console.WriteLine("[{0}] {1} :: {2} :: {3}", MethodBase.GetCurrentMethod()
                                                                   .Name, stopCalled
                                                                              ? "EXPLICITLY"
                                                                              : "TIMEOUT", Context.ConnectionId, ClientList[Context.ConnectionId]);


            WriteNumberOfConnections();

            return base.OnDisconnected(stopCalled);
        }

        public override Task OnReconnected()
        {
            if (!ClientList.ContainsKey(Context.ConnectionId))
            {
                ClientList.Add(Context.ConnectionId, null);

                Console.WriteLine("[{0}] {1}\n\r{2}\r\n=====", MethodBase.GetCurrentMethod()
                                                                             .Name, Context.ConnectionId, Context.Headers["clientName"]);
                WriteNumberOfConnections();
            }
            else
            {
                Console.WriteLine("[{0}_NotNew] {1}", MethodBase.GetCurrentMethod()
                                                                             .Name, Context.ConnectionId);
            }
            return base.OnReconnected();
        }

        public async Task JoinGroup(string groupName)
        {
            GroupList.Add(new KeyValuePair<string, string>(Context.ConnectionId, groupName));
            await Groups.Add(Context.ConnectionId, groupName);
            string message = string.Format("[Server::GroupAdd] {0} into {1}", GetSource().Value, groupName);
            Console.WriteLine(message);
            Clients.Group(groupName).displayMessage(message);
        }

        public async Task LeaveGroup(string groupName)
        {
            var groupItem = new KeyValuePair<string, string>(Context.ConnectionId, groupName);
            var record = GroupList.Any(gl => gl.Equals(groupItem));
            if (!record)
            {
                Clients.Caller.displayMessage("[Server::Rejected] There is no group");
                return;
            }
            GroupList.Remove(groupItem);
            await Groups.Remove(Context.ConnectionId, groupName);
            string message = string.Format("[Server::GroupRemoved] {0} from {1}", GetSource().Value, groupName);
            Console.WriteLine(message);
            Clients.Group(groupName).displayMessage(message);
        }
        
        private Task Alert(KeyValuePair<string, string> source)
        {
            const string methodToCall = "displayMessage";
            IClientProxy proxy = Clients.All;
            return proxy.Invoke(methodToCall, string.Format("[Server::Alert] User ({0}) Id {1} is doing something wrong", source.Key, source.Value));
        }

        public async Task<string> SetUsername(IProgress<int> progress)
        {
            Console.WriteLine("[{0}] {1} :: {2}", MethodBase.GetCurrentMethod().Name, Context.ConnectionId, Clients.CallerState.username);
            ClientList[Context.ConnectionId] = Clients.CallerState.username;

            for (int i = 0; i <= 100; i += 5)
            {
                await Task.Delay(200);
                progress.Report(i);
            }

            return Context.ConnectionId;
        }

        #region helper
        private static void WriteNumberOfConnections()
        {
            Console.WriteLine("[{0}] {1}", MethodBase.GetCurrentMethod().Name, ClientList.Count);
        }

        private KeyValuePair<string, string> GetSource()
        {
            string sourceId = Context.ConnectionId;
            string source = ClientList[sourceId];
            return new KeyValuePair<string, string>(sourceId, source);
        }
        #endregion

        public async Task<string> Send(Message input)
        {
            char command = (char)input.Command;
            string target = input.Target;
            string message = input.Content;

            var source = GetSource();

            if (message.Contains("<alert>"))
            {
                command = (char)Command.Alert;
            }

            if (message.Contains("<script>"))
            {   var mbo = new User
                          {
                              ConnectionId = source.Key,
                              UserName = source.Value
                          };
                throw new HubException("[Server::ScriptTag]", mbo);
            }


            switch ((Command)command)
            {
                case Command.AddToGroup: 
                    await JoinGroup(target);                    
                    break;
                case Command.RemoveFromGroup:
                    await LeaveGroup(target);
                    break;
                case Command.SendDirectMessage:
                    var targetClient = ClientList.FirstOrDefault(c => c.Value.Equals(target));
                    if (targetClient.Key != null)
                    {
                        string targetId = targetClient.Key;
                        await Clients.Client(targetId)
                                     .displayMessage(string.Format("[Server::Direct] {0}", message));

                        Clients.Caller.displayMessage("[Server::Confirmation] Message sent to user");
                    }
                    else
                    {
                        Clients.Caller.displayMessage("[Server::Rejected] There is no user");
                    }
                    break;
                case Command.SendGroupMessage:
                    await Clients.Group(target).displayMessage(string.Format("[Server::Group] {0}", message));
                    Clients.Caller.displayMessage("[Server::[Confirmation] Message sent to group");
                    break;
                case Command.BroadcastMessage:
                    await Clients.Others.displayMessage(string.Format("[Server::Broadcast] {0}", message));
                    Clients.Caller.displayMessage("[Server::Confirmation] Message broadcasted");
                    break;
                default:
                    await Alert(source);
                    Clients.Caller.displayMessage("[Server::Confirmation] Everyone alerted");
                    break;
            }
            return DateTime.UtcNow.ToString("s");
        }
    }
}