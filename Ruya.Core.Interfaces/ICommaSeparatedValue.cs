using System.Collections.Generic;

namespace Ruya.Core.Interfaces
{
    public interface ICommaSeparatedValue
    {
        // TODO This implementation will cause duplicate calls in derived classes. Find better way to implement it
        bool FromCSV(string fields, bool throwException);

        string ToCSV();
    }
}