using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Ruya.SignalR.Common;
using Microsoft.AspNet.SignalR.Client;

namespace Ruya.SignalR.Client
{
    internal class Operations
    {
        private readonly TextWriter _traceWriter;

        public Operations(TextWriter traceWriter)
        {
            _traceWriter = traceWriter;
        }

        public async Task RunAsync(string url)
        {
            try
            {
                await Run(url);
            }
            catch (HttpClientException httpClientException)
            {
                _traceWriter.WriteLine("HttpClientException: {0}", httpClientException.Response);
                throw;
            }
            catch (Exception exception)
            {
                _traceWriter.WriteLine("Exception: {0}", exception);
                throw;
            }
        }

        private async Task Run(string url)
        {
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly()
                                                                         .Location);
            var querystring = new Dictionary<string, string>
                              {
                                  {
                                      "version", fileVersionInfo.ProductVersion
                                  }
                              };
            var hubConnection = new HubConnection(url, querystring)
            {
                TraceWriter = _traceWriter,
                TraceLevel = TraceLevels.None
            };

            hubConnection.Headers.Add("clientName", fileVersionInfo.ProductName);


            var hubProxy = hubConnection.CreateHubProxy("HelloWorldHub");

            #region username

            hubConnection.TraceWriter.WriteLine("Please enter your nick name: ");
            var nickName = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(nickName))
            {
                throw new NotImplementedException();
            }

            hubProxy["username"] = nickName;

            #endregion

            hubProxy.On<string>("displayMessage", data =>
            {
                hubConnection.TraceWriter.WriteLine(data);
            });

            ServicePointManager.DefaultConnectionLimit = 10;

            hubConnection.Start() //x (new LongPollingTransport())
                         .ContinueWith(task =>
                         {
                             if (task.IsFaulted)
                             {
                                 if (task.Exception != null)
                                 {
                                     hubConnection.TraceWriter.WriteLine("[Client::HubConnection] There was an error opening the connection:{0}", task.Exception.GetBaseException());
                                 }
                             }
                             else
                             {
                                 hubConnection.TraceWriter.WriteLine("[Client::HubConnection] state={0}", hubConnection.State);
                                 hubConnection.TraceWriter.WriteLine("[Client::HubConnection] transport.Name={0}", hubConnection.Transport.Name);
                                 hubConnection.TraceWriter.WriteLine("[Client::HubConnection] connectionId={0}", hubConnection.ConnectionId);
                             }
                         })
                         .Wait();

            await hubProxy.Invoke<string, int>("SetUserName", percent => hubConnection.TraceWriter.WriteLine("{0}% complete", percent))
                          .ContinueWith(task =>
                                        {
                                            if (task.IsFaulted)
                                            {
                                                if (task.Exception != null)
                                                {
                                                    hubConnection.TraceWriter.WriteLine("[Client::SetUserName] There was an error occured: {0}", task.Exception.GetBaseException());
                                                }
                                            }
                                            else
                                            {
                                                hubConnection.TraceWriter.WriteLine("[Client::SetUserName] [{0}]", task.Result);
                                            }
                                        });



            string input;
            do
            {
                hubConnection.TraceWriter.WriteLine("==============================");
                hubConnection.TraceWriter.WriteLine("Enter your message: ");
                input = Console.ReadLine();
                hubConnection.TraceWriter.WriteLine("-----");
                if (input == null)
                {
                    throw new NotImplementedException();
                }
                if (input[0] == '~')
                {
                    await hubProxy.Invoke<string>("Calculate", input.Substring(1, input.Length - 1))
                                  .ContinueWith(task =>
                                                {
                                                    if (task.IsFaulted)
                                                    {
                                                        if (task.Exception != null)
                                                        {
                                                            hubConnection.TraceWriter.WriteLine("[Client::Calculate] There was an error calling calculate: {0}", task.Exception.GetBaseException());
                                                        }
                                                    }
                                                    else
                                                    {
                                                        hubConnection.TraceWriter.WriteLine("[Client::Calculate] {0}", task.Result);
                                                    }
                                                });
                }
                else
                {
                    var output = ParseMessage(input);
                    var functionName = string.Empty;
                    switch (output.Command)
                    {
                        case Command.AddToGroup:
                            functionName = "JoinGroup";
                            break;
                        case Command.RemoveFromGroup:
                            functionName = "LeaveGroup";
                            break;
                        default:
                            await hubProxy.Invoke<string>("Send", output)
                                          .ContinueWith(task =>
                                                        {
                                                            if (task.IsFaulted)
                                                            {
                                                                if (task.Exception != null)
                                                                {
                                                                    if (task.Exception.GetBaseException() is HubException)
                                                                    {
                                                                        var exception = task.Exception.GetBaseException() as HubException;
                                                                        if (exception != null)
                                                                        {
                                                                            hubConnection.TraceWriter.WriteLine("[Client::SendMessage::HubException] {0}", exception.Message);
                                                                            // TODO Cast is not working here, fix it
                                                                            var errorMessage = exception.ErrorData as User;
                                                                            if (errorMessage != null)
                                                                            {
                                                                                hubConnection.TraceWriter.WriteLine("[Client::SendMessage::HubException] UserName {0} :: ConnectionId {1}", errorMessage.UserName, errorMessage.ConnectionId);
                                                                            }
                                                                        }
                                                                    }
                                                                    else
                                                                    {
                                                                        hubConnection.TraceWriter.WriteLine("[Client::SendMessage] There was an error calling send: {0}", task.Exception.GetBaseException());
                                                                    }
                                                                }
                                                            }
                                                            else
                                                            {
                                                                hubConnection.TraceWriter.WriteLine("[Client::SendMessage] {0}", task.Result);
                                                            }
                                                        });
                            break;
                    }
                    if (!string.IsNullOrWhiteSpace(functionName))
                    {
                        try
                        {
                            await hubProxy.Invoke(functionName, output.Target)
                                          .ContinueWith(task =>
                                                        {
                                                            if (task.IsFaulted)
                                                            {
                                                                if (task.Exception != null)
                                                                {
                                                                    hubConnection.TraceWriter.WriteLine("[Client::Group] There was an error calling send: {0}", task.Exception.GetBaseException());
                                                                }
                                                            }
                                                            else
                                                            {
                                                                hubConnection.TraceWriter.WriteLine("[Client::Group] {0}", functionName);
                                                            }
                                                        });
                        }
                        catch (HubException ex)
                        {
                            hubConnection.TraceWriter.WriteLine(ex.Message);
                        }
                    }
                }
            }
            while (!string.IsNullOrWhiteSpace(input));
        }

        private static Message ParseMessage(string input)
        {
            var target = string.Empty;
            var message = string.Empty;
            var command = (Command)input[0];
            switch (command)
            {
                case Command.AddToGroup:
                case Command.RemoveFromGroup:
                    target = input.Substring(1, input.Length - 1);
                    break;
                case Command.SendDirectMessage:
                case Command.SendGroupMessage:
                    var indexOfFirstSpace = input.IndexOf(' ');
                    target = input.Substring(1, indexOfFirstSpace - 1);
                    message = input.Substring(indexOfFirstSpace + 1, input.Length - indexOfFirstSpace - 1);
                    break;
                case Command.BroadcastMessage:
                    message = input.Substring(1, input.Length - 1);
                    break;
                default:
                    throw new NotImplementedException();
            }

            var output = new Message
            {
                Command = command,
                Target = target,
                Content = message
            };
            return output;
        }
    }
}