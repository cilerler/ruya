using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Ruya.Data.Entity.Interfaces
{
    public interface IRepository<T>
        where T : class
    {
        IQueryable<T> FindAll();
        IQueryable<T> Find(Expression<Func<T, bool>> predicate);
        void Add(T newEntity);
        void Remove(T entity);
        T First(Expression<Func<T, bool>> predicate);
        IEnumerable<string> Validate(T entity);
    }
}