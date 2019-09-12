using System.Data.Entity.Core.Objects;
using Ruya.Data.Entity.Interfaces;

namespace Ruya.Data.Entity.Tests
{
    public class InMemoryUnitOfWork : IUnitOfWork
    {
        public InMemoryUnitOfWork(string connectionString) : this()
        {
        }

        public InMemoryUnitOfWork()
        {
            Committed = false;
        }

        public IObjectSet<Case> Case { get; set; }
        public bool Committed { get; set; }

        #region IUnitOfWork Members

        public void Commit()
        {
            Committed = true;
        }

        #endregion
    }
}