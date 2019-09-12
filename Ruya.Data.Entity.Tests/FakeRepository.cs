using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Ruya.Data.Entity.Interfaces;

namespace Ruya.Data.Entity.Tests
{
    public class FakeRepository<T> : IRepository<T>
        where T : class
    {
        private readonly IQueryable<T> _queryableSet;
        private readonly HashSet<T> _set;

        public FakeRepository() : this(Enumerable.Empty<T>())
        {
        }

        public FakeRepository(IEnumerable<T> entities)
        {
            _set = new HashSet<T>();
            foreach (T entity in entities)
            {
                _set.Add(entity);
            }

            _queryableSet = _set.AsQueryable();
        }

        #region IRepository<T> Members

        public IQueryable<T> FindAll()
        {
            return _queryableSet;
        }

        public IQueryable<T> Find(Expression<Func<T, bool>> predicate)
        {
            return _queryableSet.Where(predicate);
        }

        public void Add(T entity)
        {
            if (!Validate(entity)
                     .Any())
            {
                _set.Add(entity);
            }
        }

        public void Remove(T entity)
        {
            _set.Remove(entity);
        }

        public T First(Expression<Func<T, bool>> predicate)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> Validate(T entity)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}