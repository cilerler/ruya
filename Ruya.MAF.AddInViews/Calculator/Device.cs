using System.AddIn.Pipeline;

namespace Ruya.MAF.AddInViews.Calculator
{
    [AddInBase]
    public abstract class Device
    {
        public abstract string Operations { get; }
        public abstract double Operate(Operate operate);
    }
}
