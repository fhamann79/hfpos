using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class SqlCommandGateInterceptorTests
{
    private const string CompanySql = "SELECT * FROM \"Companies\" FOR UPDATE";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Before_callback_runs_once_before_release_across_reader_and_nonquery(bool readerFirst)
    {
        var release = new AsyncTestSignal();
        var reached = new AsyncTestSignal();
        var count = 0;
        using var command = new NpgsqlCommand(CompanySql);
        var interceptor = SqlCommandGateInterceptor.WaitBefore(SqlCommandMatchers.CompanyLock, release, actual =>
        {
            Assert.Same(command, actual);
            Interlocked.Increment(ref count);
            reached.Set();
        });
        using var other = new NpgsqlCommand("SELECT 1");
        await Before(interceptor, other, readerFirst);
        Assert.Equal(0, count);
        var pending = Before(interceptor, command, readerFirst);
        try
        {
            await reached.WaitAsync();
            Assert.Equal(1, count);
            Assert.False(pending.IsCompleted);
            await Before(interceptor, command, !readerFirst);
            Assert.Equal(1, count);
        }
        finally
        {
            release.Set();
            await pending;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task After_callback_precedes_signal_and_fires_once_across_reader_and_nonquery(bool readerFirst)
    {
        var signal = new AsyncTestSignal();
        var count = 0;
        var waiting = signal.WaitAsync();
        using var command = new NpgsqlCommand(CompanySql);
        var interceptor = SqlCommandGateInterceptor.SignalAfter(SqlCommandMatchers.CompanyLock, signal, actual =>
        {
            Assert.Same(command, actual);
            Assert.False(waiting.IsCompleted);
            Interlocked.Increment(ref count);
        });
        using var other = new NpgsqlCommand("SELECT 1");
        await After(interceptor, other, readerFirst);
        Assert.False(waiting.IsCompleted);
        await After(interceptor, command, readerFirst);
        await waiting;
        await After(interceptor, command, !readerFirst);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Concurrent_matches_share_one_before_and_one_after_callback()
    {
        var release = new AsyncTestSignal();
        release.Set();
        var signal = new AsyncTestSignal();
        var beforeCount = 0;
        var afterCount = 0;
        var before = SqlCommandGateInterceptor.WaitBefore(SqlCommandMatchers.CompanyLock, release,
            _ => Interlocked.Increment(ref beforeCount));
        var after = SqlCommandGateInterceptor.SignalAfter(SqlCommandMatchers.CompanyLock, signal,
            _ => Interlocked.Increment(ref afterCount));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 32).Select(async index =>
        {
            await start.Task;
            using var command = new NpgsqlCommand(CompanySql);
            await Before(before, command, index % 2 == 0);
            await After(after, command, index % 2 == 0);
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        await signal.WaitAsync();
        Assert.Equal(1, beforeCount);
        Assert.Equal(1, afterCount);
    }

    [Fact]
    public async Task Before_gate_cancellation_is_not_swallowed()
    {
        using var cancellation = new CancellationTokenSource();
        var reached = new AsyncTestSignal();
        var interceptor = SqlCommandGateInterceptor.WaitBefore(_ => true, new AsyncTestSignal(), _ => reached.Set());
        using var command = new NpgsqlCommand(CompanySql);
        var pending = interceptor.NonQueryExecutingAsync(command, null!, default, cancellation.Token).AsTask();
        await reached.WaitAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Failed_identity_callback_cannot_publish_a_success_signal()
    {
        using var cancellation = new CancellationTokenSource();
        var signal = new AsyncTestSignal();
        var waiting = signal.WaitAsync(cancellation.Token);
        var failure = new InvalidOperationException("Synthetic callback failure");
        var interceptor = SqlCommandGateInterceptor.SignalAfter(_ => true, signal, _ => throw failure);
        using var command = new NpgsqlCommand(CompanySql);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
            () => After(interceptor, command, reader: false)));
        Assert.False(waiting.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    private static async Task Before(SqlCommandGateInterceptor interceptor, DbCommand command, bool reader)
    {
        if (reader) await interceptor.ReaderExecutingAsync(command, null!, default);
        else await interceptor.NonQueryExecutingAsync(command, null!, default);
    }

    private static async Task After(SqlCommandGateInterceptor interceptor, DbCommand command, bool reader)
    {
        if (reader)
        {
            using var result = new DataTable().CreateDataReader();
            Assert.Same(result, await interceptor.ReaderExecutedAsync(command, null!, result));
        }
        else Assert.Equal(7, await interceptor.NonQueryExecutedAsync(command, null!, 7));
    }
}
