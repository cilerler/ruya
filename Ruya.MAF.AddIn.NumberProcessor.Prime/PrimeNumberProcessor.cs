using System;
using System.AddIn;
using System.Collections.Generic;
using Ruya.MAF.AddInViews.NumberProcessor;

namespace Ruya.MAF.AddIn.NumberProcessor.Prime
{
    [AddIn("Prime Number Processor", Version = "1.0.0.0", Publisher = "MAF", Description = "Returns an List<int> of prime number integers within the to/from range provided to the addin")]
    public class PrimeNumberProcessor : NumberProcessorAddInView
    {
        private HostObject _host;

        public override List<int> ProcessNumbers(int fromNumber, int toNumber)
        {
            var results = new List<int>();
            var list = new int[toNumber - fromNumber];
            double factor = 100/toNumber - fromNumber;

            // Create an array containing all integers between the two specified numbers.
            for (var i = 0; i < list.Length; i++)
            {
                list[i] = fromNumber;
                fromNumber += 1;
            }

            //find out the module for each item in list, divided by each d, where
            //d is < or == to sqrt(to)
            //mark composite with 1, and primes with 0 in mark array
            var maxDiv = (int) Math.Floor(Math.Sqrt(toNumber));

            var mark = new int[list.Length];

            for (var i = 0; i < list.Length; i++)
            {
                for (var j = 2; j <= maxDiv; j++)
                {
                    if ((list[i] != j) &&
                        (list[i]%j == 0))
                    {
                        mark[i] = 1;
                    }
                }

                _host.ReportProgress((int) (i*factor));
            }

            //get the marked primes from original array
            for (var i = 0; i < mark.Length; i++)
            {
                if (mark[i] == 0)
                {
                    results.Add(list[i]);
                }
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