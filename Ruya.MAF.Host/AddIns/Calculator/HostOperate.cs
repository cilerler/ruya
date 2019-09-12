using Ruya.MAF.HostView.Calculator;

namespace Ruya.MAF.Host.AddIns.Calculator
{
    internal class HostOperate : Operate
    {
        public HostOperate(string operation, double a, double b)
        {
            Operation = operation;
            A = a;
            B = b;
        }

        public override string Operation { get; }
        public override double A { get; }
        public override double B { get; }
    }
}