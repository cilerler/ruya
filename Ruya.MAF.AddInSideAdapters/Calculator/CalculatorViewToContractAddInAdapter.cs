using System.AddIn.Pipeline;
using Ruya.MAF.AddInViews.Calculator;
using Ruya.MAF.Contracts.Calculator;

namespace Ruya.MAF.AddInSideAdapters.Calculator
{
    [AddInAdapter]
    public class CalculatorViewToContractAddInAdapter : ContractBase, ICalculatorContract
    {
        private readonly Device _view;

        public CalculatorViewToContractAddInAdapter(Device view)
        {
            _view = view;
        }

        public virtual string GetAvailableOperations()
        {
            return _view.Operations;
        }

        public virtual double Operate(IOperateContract operate)
        {
            return _view.Operate(new OperateContractToViewAddInAdapter(operate));
        }
    }
}