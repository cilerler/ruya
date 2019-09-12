using System;
using System.Diagnostics;
using Microsoft.AspNet.SignalR.Hubs;
using Ruya.Diagnostics;

namespace Ruya.SignalR
{
    public class ErrorHandlingHubPipelineModule : HubPipelineModule
    {
#warning Refactor
        protected override void OnIncomingError(ExceptionContext exceptionContext, IHubIncomingInvokerContext invokerContext)
        {
            if (exceptionContext == null)
            {
                throw new ArgumentNullException(nameof(exceptionContext));
            }
            Tracer.Instance.TraceEvent(TraceEventType.Error, 0, "=> Exception " + exceptionContext.Error.Message);
            if (exceptionContext.Error.InnerException != null)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, "=> Inner Exception " + exceptionContext.Error.InnerException.Message);
            }
            base.OnIncomingError(exceptionContext, invokerContext);
        }
    }
}