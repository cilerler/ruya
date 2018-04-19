using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ruya.EntityFramework
{
    public class ResilientTransaction
    {
        private readonly DbContext _dbContext;

        private ResilientTransaction(DbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public static ResilientTransaction New(DbContext context) => new ResilientTransaction(context);

        public async Task ExecuteAsync(Func<Task> action)
        {
            //Use of an EF Core resiliency strategy when using multiple DbContexts within an explicit BeginTransaction():
            //See: https://docs.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency
            IExecutionStrategy strategy = _dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
                                        {
                                            using (IDbContextTransaction transaction = _dbContext.Database.BeginTransaction())
                                            {
                                                await action();
                                                transaction.Commit();
                                            }
                                        });
        }
    }
}
