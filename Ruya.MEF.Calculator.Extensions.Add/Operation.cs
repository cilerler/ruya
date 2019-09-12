using System.ComponentModel.Composition;
using Ruya.MEF.Calculator.Interfaces;

namespace Ruya.MEF.Calculator.Extensions.Add
{
    [Export(typeof(IOperation)), ExportMetadata("Symbol", '+')]
    internal sealed class Operation : IOperation
    {
        public int Operate(int left, int right)
        {
            return left + right;
        }
    }
}
