using System.AddIn;
using System.Collections.Generic;
using Ruya.MAF.AddInViews.NumberProcessor;

namespace Ruya.MAF.AddIn.NumberProcessor.Loop
{
    [AddIn("Loop Number Processor", Version = "1.0.0.0", Publisher = "MAF", Description = "Returns an List<int> of looped number integers within the to/from range provided to the addin")]
    public class LoopNumberProcessor : NumberProcessorAddInView
    {
        private HostObject _host;

        public override List<int> ProcessNumbers(int fromNumber, int toNumber)
        {
            var results = new List<int>();

            double factor = 100/toNumber - fromNumber;

            for (int i = fromNumber; i < toNumber; i++)
            {
                _host.ReportProgress((int) (i*factor));
                results.Add(i);
            }
            _host.ReportProgress(100);
            return results;
        }

        public override void Initialize(HostObject hostObject)
        {
            _host = hostObject;
        }
    }
}