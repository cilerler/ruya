using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Ruya.MEF.Calculator.Interfaces;

namespace Ruya.MEF.Calculator
{
    [Export(typeof(ICalculator))]
    internal sealed class SimpleCalculator : ICalculator
    {
        [ImportMany]
        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        private IEnumerable<Lazy<IOperation, IOperationData>> _operations;

        public string Calculate(string input)
        {
            int left;
            int right;
            int fn = FindFirstNonDigit(input); //finds the operator
            if (fn < 0)
            {
                return "Could not parse command.";
            }

            try
            {
                //separate out the operands
                left = int.Parse(input.Substring(0, fn));
                right = int.Parse(input.Substring(fn + 1));
            }
            catch
            {
                return "Could not parse command.";
            }

            char operation = input[fn];

            foreach (Lazy<IOperation, IOperationData> i in _operations.Where(i => i.Metadata.Symbol.Equals(operation)))
            {
                return i.Value.Operate(left, right)
                        .ToString();
            }
            return "Operation Not Found!";
        }

        private static int FindFirstNonDigit(string s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                if (!(char.IsDigit(s[i])))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
