using System.Data.Entity;

namespace Ruya.Data.Entity
{
    public partial class Entities : DbContext
    {
        public Entities(string connectionString) : base(connectionString)
        {
        }
    }
}