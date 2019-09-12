using System.AddIn.Pipeline;
using Ruya.MAF.Contracts.Calculator;
using Ruya.MAF.HostView.Calculator;

namespace Ruya.MAF.HostSideAdapters.Calculator
{
    public class OperateViewToContractHostAdapter : ContractBase, IOperateContract
    {
        private readonly Operate _view;

        public OperateViewToContractHostAdapter(Operate view)
        {
            _view = view;
        }

        public virtual string GetOperation()
        {
            return _view.Operation;
        }

        public virtual double GetA()
        {
            return _view.A;
        }

        public virtual double GetB()
        {
            return _view.B;
        }
    }
}