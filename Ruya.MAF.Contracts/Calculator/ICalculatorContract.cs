using System.AddIn.Contract;
using System.AddIn.Pipeline;

namespace Ruya.MAF.Contracts.Calculator
{
    [AddInContract]
    public interface ICalculatorContract : IContract
    {
        string GetAvailableOperations();
        double Operate(IOperateContract operate);
    }
}