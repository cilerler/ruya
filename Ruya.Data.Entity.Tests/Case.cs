using System.Collections.Generic;

namespace Ruya.Data.Entity.Tests
{
    public sealed class Case
    {
        public Case()
        {
            FieldReports = new HashSet<FieldReport>();
        }

        public ICollection<FieldReport> FieldReports { get; set; }
        public string CaseNumberFromFieldReport { get; set; }
        public string CustomerIdFromFieldReport { get; set; }
    }
}