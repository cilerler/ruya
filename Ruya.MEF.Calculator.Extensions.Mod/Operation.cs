using System.ComponentModel.Composition;
using Ruya.MEF.Calculator.Interfaces;

namespace Ruya.MEF.Calculator.Extensions.Mod
{
    [Export(typeof(IOperation)), ExportMetadata("Symbol", '%')]
    public class Operation : IOperation
    {
        public int Operate(int left, int right)
        {
            return left % right;
        }
    }
}
