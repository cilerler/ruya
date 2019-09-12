using System.Collections.Generic;
using System.Linq;

namespace Ruya.Data.Entity.Tests
{
    public static class ObjectMother
    {
        public static IEnumerable<Case> CreateCases()
        {

            yield return new Case()
                         {
                             FieldReports = CreateFieldReports().ToList(),
                             CaseNumberFromFieldReport = "XYZ",
                             CustomerIdFromFieldReport = "ABC"
                         };
        }

        public static IEnumerable<FieldReport> CreateFieldReports()
        {
            yield return new FieldReport() { EnforcementClassCode = "321" };
        }
    }
}