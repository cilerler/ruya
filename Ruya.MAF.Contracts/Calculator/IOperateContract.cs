using System.AddIn.Contract;

namespace Ruya.MAF.Contracts.Calculator
{
    public interface IOperateContract : IContract
    {
        string GetOperation();
        double GetA();
        double GetB();
    }
}