using System.AddIn.Pipeline;
using System.Collections.Generic;
using Ruya.MAF.AddInViews.NumberProcessor;
using Ruya.MAF.Contracts.NumberProcessor;

namespace Ruya.MAF.AddInSideAdapters.NumberProcessor
{
    /// <summary>
    ///     Adapter use to talk to AddIn <see cref="INumberProcessorContract">AddIn Contract</see>
    /// </summary>
    [AddInAdapter]
    public class NumberProcessorViewToContractAdapter : ContractBase, INumberProcessorContract
    {
        private readonly NumberProcessorAddInView _view;

        public NumberProcessorViewToContractAdapter(NumberProcessorAddInView view)
        {
            _view = view;
        }

        public List<int> ProcessNumbers(int fromNumber, int toNumber)
        {
            return _view.ProcessNumbers(fromNumber, toNumber);
        }

        public void Initialize(IHostObjectContract hostObject)
        {
            _view.Initialize(new HostObjectContractToViewAddInAdapter(hostObject));
        }
    }
}