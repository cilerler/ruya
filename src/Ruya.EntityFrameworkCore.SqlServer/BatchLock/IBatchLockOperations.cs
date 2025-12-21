using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.EntityFrameworkCore.SqlServer.BatchLock;

/// <summary>
/// Provides batch locking operations using SELECT FOR UPDATE pattern with ROWLOCK, UPDLOCK, READPAST hints.
/// </summary>
public interface IBatchLockOperations
{
	/// <summary>
	/// Locks a batch of rows and returns them for processing.
	/// Uses ROWLOCK, UPDLOCK, READPAST hints for concurrent processing without blocking.
	/// </summary>
	/// <typeparam name="T">The entity type to return.</typeparam>
	/// <param name="options">Batch lock configuration options.</param>
	/// <param name="callerMethod">Name of the calling method.</param>
	/// <returns>Queryable of locked rows for deferred execution.</returns>
	IQueryable<T> SelectForUpdate<T>(BatchLockOptions options, [System.Runtime.CompilerServices.CallerMemberName] string callerMethod = "") where T : class;

	/// <summary>
	/// Locks a batch of rows and returns only the primary key values.
	/// Uses ROWLOCK, UPDLOCK, READPAST hints for concurrent processing without blocking.
	/// More efficient than SelectForUpdate when you only need the IDs.
	/// </summary>
	/// <typeparam name="TKey">The type of the primary key (e.g., int, long, Guid).</typeparam>
	/// <param name="options">Batch lock configuration options.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <param name="callerMethod">Name of the calling method.</param>
	/// <returns>List of primary key values of the locked rows.</returns>
	Task<List<TKey>> SelectForUpdateKeysAsync<TKey>(BatchLockOptions options, CancellationToken cancellationToken = default, [System.Runtime.CompilerServices.CallerMemberName] string callerMethod = "");
}
