using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pos.Backend.Api.Tests.Infrastructure;

internal sealed class CommandCountingInterceptor : DbCommandInterceptor
{
    private int _readerCount;

    public int ReaderCount => Volatile.Read(ref _readerCount);

    public void Reset() => Interlocked.Exchange(ref _readerCount, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Interlocked.Increment(ref _readerCount);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _readerCount);
        return ValueTask.FromResult(result);
    }
}
