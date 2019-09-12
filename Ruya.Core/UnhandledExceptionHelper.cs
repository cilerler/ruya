using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Ruya.Core.Properties;

namespace Ruya.Core
{
    // TEST class UnhandledExceptionHelper //! implemented in CONSOLE
    // COMMENT class UnhandledExceptionHelper
    [Serializable]
    public class UnhandledExceptionHelper
    {
        public UnhandledExceptionHelper()
        {
            ApplicationDomain = AppDomain.CurrentDomain;
        }

        public AppDomain ApplicationDomain { get; }

        public void Register()
        {
            ApplicationDomain.UnhandledException += UnhandledExceptionHandler;
        }

        public void Unregister()
        {
            ApplicationDomain.UnhandledException -= UnhandledExceptionHandler;
        }

        private static string GetExceptionMessage(Exception exception)
        {
            const char mainSeparator = '=';
            const char subSeparator = '-';
            var contents = new StringBuilder();
            contents.AppendLine(new string(mainSeparator, 79));
            if (exception != null)
            {
                contents.AppendLine(string.Format(CultureInfo.InvariantCulture, Resources.UnhandledExceptionHelper_UnhandledExceptionHandler_Exception, exception.Message));
                contents.AppendLine(new string(subSeparator, 39));
                contents.AppendLine(exception.StackTrace);
                if (exception.InnerException != null)
                {
                    contents.AppendLine(GetExceptionMessage(exception.InnerException));
                }
            }
            return contents.ToString();
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }
            const char separator = '*';
            var contents = new StringBuilder();
            contents.AppendLine(new string(separator, 79));
            
            contents.AppendLine(string.Format(CultureInfo.InvariantCulture, Resources.UnhandledExceptionHelper_UnhandledExceptionHandler_IsTerminating, args.IsTerminating));

            var exception = args.ExceptionObject as Exception;
            contents.AppendLine(GetExceptionMessage(exception));

            contents.AppendLine(new string(separator, 79));

            string path = string.Format(CultureInfo.InvariantCulture, Resources.UnhandledExceptionHelper_UnhandledExceptionHandler_FileName, DateTimeHelper.GetDateTimeUtcNowWithUtcOffset());
            try
            {
                File.WriteAllText(path, contents.ToString());
            }
            catch (Exception ex)
            {
                throw new CoreException(ex.Message, ex);
            }
            finally
            {
                Environment.Exit(-1);
            }
        }
    }
}