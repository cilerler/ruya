using System;
using System.Diagnostics;
using Microsoft.AspNet.SignalR.Hubs;
using Ruya.Diagnostics;

namespace Ruya.SignalR
{
    public class LoggingHubPipelineModule : HubPipelineModule
    {
#warning Refactor
        protected override bool OnBeforeIncoming(IHubIncomingInvokerContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            // HARD-CODED constant
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, "=> Invoking [" + context.MethodDescriptor.Name + "] on hub [" + context.MethodDescriptor.Hub.Name + "]");
            return base.OnBeforeIncoming(context);
        }
        protected override bool OnBeforeOutgoing(IHubOutgoingInvokerContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            string contextSignal = context.Signal;
            if (context.Signals != null)
            {
                contextSignal = "+" + string.Join(",", context.Signals);
            }
            // HARD-CODED constant
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, "<= Invoking [" + context.Invocation.Method + "] on clients|groups [" + contextSignal + "]");
            return base.OnBeforeOutgoing(context);
        }
    }
}