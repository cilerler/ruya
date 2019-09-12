using System.AddIn.Pipeline;
using System.Collections.Generic;
using Ruya.MAF.Contracts.NumberProcessor;
using Ruya.MAF.HostView.NumberProcessor;

namespace Ruya.MAF.HostSideAdapters.NumberProcessor
{
    /// <summary>
    ///     Adapter use to talk to <see cref="NumberProcessorHostView">Host View</see>
    /// </summary>
    [HostAdapter]
    public class NumberProcessorContractToViewHostAdapter : NumberProcessorHostView
    {
        private ContractHandle _contractHandle;
        private readonly INumberProcessorContract _contract;

        public NumberProcessorContractToViewHostAdapter(INumberProcessorContract contract)
        {
            _contract = contract;
            _contractHandle = new ContractHandle(contract);
        }

        public override List<int> ProcessNumbers(int fromNumber, int toNumber)
        {
            return _contract.ProcessNumbers(fromNumber, toNumber);
        }

        public override void Initialize(HostObject host)
        {
            var hostAdapter = new HostObjectViewToContractHostAdapter(host);
            _contract.Initialize(hostAdapter);
        }
    }
}