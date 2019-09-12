using System.AddIn.Contract;

namespace Ruya.MAF.Contracts.NumberProcessor
{
    /// <summary>
    /// The actual Host contract that is implemented by the
    /// <see cref="Ruya.MAF.HostInSideAdapters.NumberProcessor.HostObjectViewToContractHostAdapter">Host Adapter</see>
    /// Which enabled the AddIn to talk back to the host
    /// </summary>
    public interface IHostObjectContract : IContract
    {
        void ReportProgress(int progressPercent);
    }
}
