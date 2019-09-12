using System;
using System.ComponentModel;
using System.Diagnostics;
using Menu;
using Microsoft.Practices.EnterpriseLibrary.ExceptionHandling;
using Microsoft.Practices.EnterpriseLibrary.Logging;

namespace Ruya.EL.Host
{
    internal class Program
    {
        private static ExceptionManager _exManager;

        private static void Main()
        {
            Debug.Listeners.Add(new ConsoleTraceListener());

            LoggingConfiguration loggingConfiguration = Logging.BuildProgrammaticConfig();
            Logger.SetLogWriter(new LogWriter(loggingConfiguration));

            // Create the default ExceptionManager object from the configuration settings.
            //ExceptionPolicyFactory policyFactory = new ExceptionPolicyFactory();
            //exManager = policyFactory.CreateManager();

            // Create the default ExceptionManager object programatically
            _exManager = ExceptionHandling.BuildExceptionManagerConfig(Logger.Writer);

            // Create an ExceptionPolicy to illustrate the static HandleException method
            ExceptionPolicy.SetExceptionManager(_exManager);

            new MenuCollection("Menu", true, Logging.SimpleLogWrite, Logging.SimpleLogWriteDecalarative, WithWrapExceptionShielding, WithWrapExceptionShieldingStatic, WithWrapExceptionShielding2).Run();
            Logger.Reset();
        }

        #region Menu Items

        [Description("Behavior After Applying Exception Shielding with a Wrap Handler")]
        internal static void WithWrapExceptionShielding()
        {
            _exManager.Process(() =>
                               {
                                   throw new ArgumentNullException();
                               }, "ExceptionShielding");
            _exManager.Process(() =>
                               {
                                   throw new ArgumentOutOfRangeException();
                               }, "ExceptionShielding");
            _exManager.Process(() =>
                               {
                                   throw new Exception("E");
                               }, "ExceptionShielding");
        }

        [Description("Behavior After Applying Exception Shielding with a Wrap Handler 2")]
        internal static void WithWrapExceptionShielding2()
        {
            _exManager.Process(() =>
                               {
                                   throw new Exception("E");
                               }, "ExceptionShielding");
        }

        [Description("Using the static ExceptionPolicy class")]
        internal static void WithWrapExceptionShieldingStatic()
        {
            try
            {
                throw new ArgumentOutOfRangeException();
            }
            catch (Exception ex)
            {
                Exception exceptionToThrow;
                //x bool rethrow = ExceptionPolicy.HandleException(ex, "ExceptionShielding", out exceptionToThrow);
                bool rethrow = _exManager.HandleException(ex, "ExceptionShielding", out exceptionToThrow);
                if (rethrow)
                {
                    // Exception policy setting is "ThrowNewException"
                    if (exceptionToThrow == null)
                    {
                        throw;
                    }
                    throw exceptionToThrow;
                }
            }

            #endregion
        }
    }
}