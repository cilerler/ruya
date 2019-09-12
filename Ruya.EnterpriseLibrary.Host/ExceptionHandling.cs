using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Practices.EnterpriseLibrary.ExceptionHandling;
using Microsoft.Practices.EnterpriseLibrary.ExceptionHandling.Logging;
using Microsoft.Practices.EnterpriseLibrary.Logging;

namespace Ruya.EL.Host
{
    public class ExceptionHandling
    {
        internal static ExceptionManager BuildExceptionManagerConfig(LogWriter logWriter)
        {
            var log = new List<ExceptionPolicyEntry>
                      {
                          new ExceptionPolicyEntry(typeof (ArgumentNullException), PostHandlingAction.None, new IExceptionHandler[]
                                                                                                            {
                                                                                                                new LoggingExceptionHandler("Exception", 8000, TraceEventType.Critical, "Policy based exception logging", 5, typeof (TextExceptionFormatter), logWriter),
                                                                                                                new ReplaceHandler("Application error will be ignored and processing will continue.", typeof (MyCustomException))
                                                                                                            }),
                          new ExceptionPolicyEntry(typeof (ArgumentOutOfRangeException), PostHandlingAction.ThrowNewException, new IExceptionHandler[]
                                                                                                                               {
                                                                                                                                   new WrapHandler("Application Error. Please contact your administrator.", typeof (MyCustomException))
                                                                                                                               }),
                          new ExceptionPolicyEntry(typeof (Exception), PostHandlingAction.NotifyRethrow, new IExceptionHandler[]
                                                                                                         {
                                                                                                             new LoggingExceptionHandler("Exception", 8001, TraceEventType.Critical, "Policy based exception logging", 5, typeof (TextExceptionFormatter), logWriter)
                                                                                                         })
                      };

            var policies = new List<ExceptionPolicyDefinition>
                           {
                               new ExceptionPolicyDefinition("ExceptionShielding", log)
                           };

            var manager = new ExceptionManager(policies);
            return manager;
        }
    }
}