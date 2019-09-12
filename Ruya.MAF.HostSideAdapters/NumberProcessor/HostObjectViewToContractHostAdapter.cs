using System.AddIn.Pipeline;
using Ruya.MAF.Contracts.NumberProcessor;
using Ruya.MAF.HostView.NumberProcessor;

namespace Ruya.MAF.HostSideAdapters.NumberProcessor
{
    /// <summary>
    ///     Allows Host side adapter to talk back to HostView
    /// </summary>
    public class HostObjectViewToContractHostAdapter : ContractBase, IHostObjectContract
    {
        private readonly HostObject _view;

        public HostObjectViewToContractHostAdapter(HostObject view)
        {
            _view = view;
        }

        public void ReportProgress(int progressPercent)
        {
            _view.ReportProgress(progressPercent);
        }
    }
}