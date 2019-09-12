using System.AddIn;
using System.Collections.Generic;
using Ruya.MAF.AddInViews.NumberProcessor;

namespace Ruya.MAF.AddIn.NumberProcessor.Fibonacci
{
    [AddIn("Fibonacci Number Processor", Version = "1.0.0.0", Publisher = "MAF", Description = "Returns an List<int> of fibonacci number integers within the to/from range provided to the addin")]
    public class FibonacciNumberProcessor : NumberProcessorAddInView
    {
        private HostObject _host;

        public static int Fibonacci(int n)
        {
            if (n == 0 ||
                n == 1)
            {
                return n;
            }
            return Fibonacci(n - 1) + Fibonacci(n - 2);
        }

        public override List<int> ProcessNumbers(int fromNumber, int toNumber)
        {
            var results = new List<int>();

            double factor = 100/toNumber - fromNumber;

            for (int i = fromNumber; i < toNumber; i++)
            {
                _host.ReportProgress((int) (i*factor));
                results.Add(Fibonacci(i));
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