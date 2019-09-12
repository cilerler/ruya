using System;
using System.AddIn;
using System.Threading;
using Ruya.MAF.AddInViews.Calculator;

namespace Ruya.MAF.AddIns
{
    [AddIn("Calculator AddIn", Description = "Simple calculator prototype", Publisher = "MAF", Version = "1.0.0.0")]
    public class Calculator : Device
    {
        public override string Operations => "+ - * / ** throw";

        public override double Operate(Operate operation)
        {
            switch (operation.Operation)
            {
                case "+":
                    return operation.A + operation.B;
                case "-":
                    return operation.A - operation.B;
                case "*":
                    return operation.A*operation.B;
                case "/":
                    return operation.A/operation.B;
                case "**":
                    return Math.Pow(operation.A, operation.B);
                case "throw":
                    ThrowOnChildThread();
                    return 0;
                default:
                    throw new InvalidOperationException("This add-in does not support: " + operation.Operation);
            }
        }

        internal void ThrowOnChildThread()
        {
            ThreadStart ts = delegate
                             {
                                 throw new Exception();
                             };
            new Thread(ts).Start();
        }
    }
}