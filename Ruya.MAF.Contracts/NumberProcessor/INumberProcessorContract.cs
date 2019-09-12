using System.AddIn.Contract;
using System.AddIn.Pipeline;
using System.Collections.Generic;

namespace Ruya.MAF.Contracts.NumberProcessor
{
    /// <summary>
    /// The actual AddIn contract that is implemented by the
    /// <see cref="Ruya.MAF.AddInSideAdapters.NumberProcessor.NumberProcessorViewToContractAdapter">AddIn Adapter</see>
    /// </summary>
    [AddInContract]
    public interface INumberProcessorContract : IContract
    {
        List<int> ProcessNumbers(int fromNumber, int toNumber);
        void Initialize(IHostObjectContract hostObject);
    }
}