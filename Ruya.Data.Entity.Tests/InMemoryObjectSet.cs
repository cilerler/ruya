using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Entity.Core.Objects;
using System.Linq;
using System.Linq.Expressions;

namespace Ruya.Data.Entity.Tests
{
    public class InMemoryObjectSet<T> : IObjectSet<T>
        where T : class
    {
        private readonly IQueryable<T> _queryableSet;
        private readonly HashSet<T> _set;

        public InMemoryObjectSet() : this(Enumerable.Empty<T>())
        {
        }

        public InMemoryObjectSet(IEnumerable<T> entities)
        {
            _set = new HashSet<T>();
            foreach (T entity in entities)
            {
                _set.Add(entity);
            }

            _queryableSet = _set.AsQueryable();
        }

        #region IObjectSet<T> Members

        public void AddObject(T entity)
        {
            _set.Add(entity);
        }

        public void Attach(T entity)
        {
            AddObject(entity);
        }

        public void DeleteObject(T entity)
        {
            _set.Remove(entity);
        }

        public void Detach(T entity)
        {
            DeleteObject(entity);
        }

        public Type ElementType => _queryableSet.ElementType;
        public Expression Expression => _queryableSet.Expression;
        public IQueryProvider Provider => _queryableSet.Provider;

        public IEnumerator<T> GetEnumerator()
        {
            return _set.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }
}