using System.Linq;

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
}
