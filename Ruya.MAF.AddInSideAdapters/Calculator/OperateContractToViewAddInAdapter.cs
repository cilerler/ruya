using System.AddIn.Pipeline;
using Ruya.MAF.AddInViews.Calculator;
using Ruya.MAF.Contracts.Calculator;

namespace Ruya.MAF.AddInSideAdapters.Calculator
{
    public class OperateContractToViewAddInAdapter : Operate
    {
        private ContractHandle _handle;
        private readonly IOperateContract _contract;

        public OperateContractToViewAddInAdapter(IOperateContract contract)
        {
            _contract = contract;
            _handle = new ContractHandle(contract);
        }

        public override string Operation => _contract.GetOperation();

        public override double A => _contract.GetA();

        public override double B => _contract.GetB();
    }
}