namespace Ruya.MAF.AddInViews.NumberProcessor
{
    /// <summary>
    /// Abstract class that should be inherited by an object that needs to communicate
    /// between the host Contract to View adapter <see cref="Ruya.MAF.AddInSideAdapters.NumberProcessor.HostObjectContractToViewAddInAdapter">HostObjectContractToViewAddInAdapter</see>
    /// </summary>
    public abstract class HostObject
    {
        public abstract void ReportProgress(int progressPercent);
    }
}