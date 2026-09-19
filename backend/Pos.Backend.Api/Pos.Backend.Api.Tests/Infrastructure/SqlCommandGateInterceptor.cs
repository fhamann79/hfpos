using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pos.Backend.Api.Tests.Infrastructure;

internal sealed class AsyncTestSignal
{
    private readonly TaskCompletionSource _source = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public void Set() => _source.TrySetResult();

    public Task WaitAsync(CancellationToken cancellationToken = default)
        => _source.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
}

internal sealed class SqlCommandGateInterceptor : DbCommandInterceptor
{
    private readonly Func<string, bool> _matches;
    private readonly Func<CancellationToken, Task>? _beforeExecution;
    private readonly Action? _afterExecution;
    private int _beforeMatched;
    private int _afterMatched;

    private SqlCommandGateInterceptor(
        Func<string, bool> matches,
        Func<CancellationToken, Task>? beforeExecution,
        Action? afterExecution)
    {
        _matches = matches;
        _beforeExecution = beforeExecution;
        _afterExecution = afterExecution;
    }

    public static SqlCommandGateInterceptor WaitBefore(
        Func<string, bool> matches,
        AsyncTestSignal signal)
        => new(matches, signal.WaitAsync, afterExecution: null);

    public static SqlCommandGateInterceptor SignalAfter(
        Func<string, bool> matches,
        AsyncTestSignal signal)
        => new(matches, beforeExecution: null, signal.Set);

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (_beforeExecution is not null
            && _matches(command.CommandText)
            && Interlocked.Exchange(ref _beforeMatched, 1) == 0)
        {
            await _beforeExecution(cancellationToken);
        }

        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (_afterExecution is not null
            && _matches(command.CommandText)
            && Interlocked.Exchange(ref _afterMatched, 1) == 0)
        {
            _afterExecution();
        }

        return ValueTask.FromResult(result);
    }
}

internal static class SqlCommandMatchers
{
    public static bool OpenCashSessionForUpdate(string commandText)
        => ContainsAll(
            commandText,
            "FROM \"CashSessions\"",
            "WHERE \"CompanyId\"",
            "AND \"OpenedByUserId\"",
            "FOR UPDATE");

    public static bool CashSessionByIdForUpdate(string commandText)
        => ContainsAll(commandText, "FROM \"CashSessions\"", "WHERE \"Id\"", "FOR UPDATE");

    public static bool SaleByIdForUpdate(string commandText)
        => ContainsAll(commandText, "FROM \"Sales\"", "WHERE \"Id\"", "FOR UPDATE");

    private static bool ContainsAll(string commandText, params string[] fragments)
        => fragments.All(fragment => commandText.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
