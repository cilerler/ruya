using System.AddIn.Pipeline;
using Ruya.MAF.AddInViews.NumberProcessor;
using Ruya.MAF.Contracts.NumberProcessor;

namespace Ruya.MAF.AddInSideAdapters.NumberProcessor
{
    /// <summary>
    ///     Allows AddIn adapter to talk back to HostView
    /// </summary>
    public class HostObjectContractToViewAddInAdapter : HostObject
    {
        private ContractHandle _handle;
        private readonly IHostObjectContract _contract;

        public HostObjectContractToViewAddInAdapter(IHostObjectContract contract)
        {
            _contract = contract;
            _handle = new ContractHandle(contract);
        }

        public override void ReportProgress(int progressPercent)
        {
            _contract.ReportProgress(progressPercent);
        }
    }
}