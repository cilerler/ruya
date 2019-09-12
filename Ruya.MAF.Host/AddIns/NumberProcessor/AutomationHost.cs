using System.IO;
using Ruya.MAF.HostView.NumberProcessor;

namespace Ruya.MAF.Host.AddIns.NumberProcessor
{
    /// <summary>
    ///     A wrapper class that allows the reported progress within the
    ///     <see cref="Ruya.MAF.HostView.NumberProcessor.HostObject">host view </see> to display
    ///     progress on a ProgressBar within the host app
    /// </summary>
    internal class AutomationHost : HostObject
    {
        private readonly TextWriter _progress;

        public AutomationHost(TextWriter progress)
        {
            _progress = progress;
        }

        /// <summary>
        ///     Override <see cref="Ruya.MAF.HostView.NumberProcessor.HostObject">host view </see>
        ///     ReportProgress method
        /// </summary>
        /// <param name="progressPercent"></param>
        public override void ReportProgress(int progressPercent)
        {
            // Update the UI on the UI thread.
            _progress.WriteLine("Complete %{0}", progressPercent);
        }
    }
}