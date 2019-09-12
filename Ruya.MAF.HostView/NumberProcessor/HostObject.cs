namespace Ruya.MAF.HostView.NumberProcessor
{
    /// <summary>
    /// Abstract base class that should be inherited by a class within the host
    /// application that can make use of the reported progress
    /// </summary>
    public abstract class HostObject
    {
        public abstract void ReportProgress(int progressPercent);
    }
}