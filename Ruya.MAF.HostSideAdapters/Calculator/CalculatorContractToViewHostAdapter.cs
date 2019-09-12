using System.AddIn.Pipeline;
using Ruya.MAF.Contracts.Calculator;
using Ruya.MAF.HostView.Calculator;

namespace Ruya.MAF.HostSideAdapters.Calculator
{
    [HostAdapter]
    public class CalculatorContractToViewHostAdapter : Device
    {
        private ContractHandle _handle;
        private readonly ICalculatorContract _contract;

        public CalculatorContractToViewHostAdapter(ICalculatorContract contract)
        {
            _contract = contract;
            _handle = new ContractHandle(contract);
        }

        public override string Operations => _contract.GetAvailableOperations();

        public override double Operate(Operate operate)
        {
            return _contract.Operate(new OperateViewToContractHostAdapter(operate));
        }
    }
}
