using System.AddIn.Pipeline;
using System.Collections.Generic;

namespace Ruya.MAF.AddInViews.NumberProcessor
{
    /// <summary>
    ///     Abstract base class that should be inherited by all AddIns
    /// </summary>
    [AddInBase]
    public abstract class NumberProcessorAddInView
    {
        public abstract List<int> ProcessNumbers(int fromNumber, int toNumber);
        public abstract void Initialize(HostObject hostObject);
    }
}