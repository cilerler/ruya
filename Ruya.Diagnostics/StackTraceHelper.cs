using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ruya.Diagnostics
{
    public enum StackTraceCaller
    {
        None = 0,
        This = 1,
        Caller = 2,
        CallersCaller = 3,
        CallersCallersCaller = 4
    }

    public static class StackTraceHelper
    {
        /// <summary>
        ///     Gets the specified stack frame's method 
        /// </summary>
        /// <param name="index">The index of the stack frame requested.  1 represents caller of this method</param>
        /// <returns>The specified method of executing stack frame.</returns>
        /// <seealso cref="System.Environment.StackTrace" />
        public static MethodBase GetMethod(int index)
        {
            int confirmedIndex = index;
            var stackTrace = new StackTrace();
            if (confirmedIndex>stackTrace.FrameCount-1)
            {
                confirmedIndex = stackTrace.FrameCount-1;
            }
            return stackTrace.GetFrame(confirmedIndex).GetMethod();
        }

        public static MethodBase GetMethod(StackTraceCaller index)
        {
            const byte self = 1;
            return GetMethod((int)index + self);            
        }

#if NET45_OR_GREATER

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1026:DefaultParametersShouldNotBeUsed")]
        public static Dictionary<string, string> GetCallerInformation([CallerLineNumber] int callerLineNumber = 0, [CallerFilePath] string callerFilePath = null, [CallerMemberName] string callerMemberName = null)
        {
            var output = new Dictionary<string, string>
                         {
                             {
                                 nameof(callerLineNumber), callerLineNumber.ToString(CultureInfo.InvariantCulture)
                             },
                             {
                                 nameof(callerFilePath), callerFilePath
                             },
                             {
                                 nameof(callerMemberName), callerMemberName
                             }
                         };
            return output;
        }
#endif

    }
}
