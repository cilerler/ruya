using System.ComponentModel;
using System.Diagnostics;

namespace Ruya.Diagnostics
{
    public static class DebuggerHelper
    {
        /// <summary>
        /// Signals <see cref="F:System.Diagnostics.Debugger" />.  If it is not attached, lauches it.
        /// </summary>
        [Description("Debugger Caller")]        
        public static void CallDebugger()
        {
            if (!Debugger.IsAttached)
            {
                Debugger.Launch();
            }
            else
            {
                Debugger.Break();
            }
        }
    }
}
