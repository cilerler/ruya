using System.Collections.Generic;

namespace Ruya.MAF.HostView.NumberProcessor
{
    /// <summary>
    /// Abstract base class that should be inherited by the Host view
    /// </summary>
    public abstract class NumberProcessorHostView
    {
        public abstract List<int> ProcessNumbers(int fromNumber, int toNumber);

        public abstract void Initialize(HostObject host);
    }
}
