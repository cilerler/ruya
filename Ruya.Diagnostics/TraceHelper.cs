using System;
using System.Diagnostics;

namespace Ruya.Diagnostics
{
    public static class TraceHelper
    {
        public static bool StartLogicalOperation(object operationId)
        {
            bool logicalOperation;
            try
            {
                Trace.CorrelationManager.StartLogicalOperation(operationId);
                logicalOperation = true;
            }
            catch (ArgumentNullException argumentNullException)
            {
                logicalOperation = false;
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, "There is an issue occured while starting logical operation" + argumentNullException.Message);
            }
            return logicalOperation;
        }

        public static bool StopLogicalOperation()
        {
            bool output;
            try
            {
                Trace.CorrelationManager.StopLogicalOperation();
                output = true;
            }
            catch (InvalidOperationException invalidOperationException)
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, "There is an issue occured while stopping logical operation" + invalidOperationException.Message);
                output = false;
            }
            return output;
        }
    }
}
