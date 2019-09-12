using System.Diagnostics;
using Ruya.Core;
using Ruya.Diagnostics;

namespace Ruya.Host
{
    public static class Tests
    {
        public static void RunConsoleTests()
        {
            RollingXmlTest.Run(1500);

            // Core exception test, validate at Output window
            ExceptionTest.ThrowCoreException();

            System.Console.ReadLine();

            // Unhandled
            var unhandledExceptionHelper = new UnhandledExceptionHelper();
            unhandledExceptionHelper.Register();
            UnhandledExceptionTest.ThrowException();

            Tracer.Instance.CloseAll();
        }
    }


    internal static class ExceptionTest
    {
        public static void ThrowCoreException()
        {
            try
            {
                throw new CoreException();
            }
            catch (CoreException)
            {
            }
        }
    }

    /// <summary>
    ///     Creates traces
    /// </summary>
    internal static class RollingXmlTest
    {
        public static void Run(int number)
        {
            for (var counter = 0; counter < number; counter++)
            {
                Tracer.Instance.TraceEvent(EnumHelper.GetRandomEnumItem<TraceEventType>(), 0, StringHelper.GenerateRandomText(12, StringFeatures.LetterUpper | StringFeatures.LetterLower | StringFeatures.Number));
            }
        }
    }


    internal static class UnhandledExceptionTest
    {
        public static void ThrowException()
        {
            throw new CoreException();
        }
    }
}