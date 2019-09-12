namespace Ruya.MAF.HostView.Calculator
{
    public abstract class Device
    {
        public abstract string Operations { get; }
        public abstract double Operate(Operate operate);
    }
}
