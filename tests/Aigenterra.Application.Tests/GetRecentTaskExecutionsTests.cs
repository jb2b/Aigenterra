using Aigenterra.Application.Common;
using Aigenterra.Application.Executions;
using Aigenterra.Application.Executions.Ports;

namespace Aigenterra.Application.Tests;

public sealed class GetRecentTaskExecutionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Invalid_limit_does_not_call_reader(int limit)
    {
        var reader = new Reader();
        var result = await new GetRecentTaskExecutions(reader).ExecuteAsync(new(limit));
        Assert.Equal(ApplicationErrorType.Validation, result.Error!.Type);
        Assert.Equal("invalid_execution_limit", result.Error.Code);
        Assert.Null(reader.Limit);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Valid_limit_passes_cancellation_and_preserves_reader_order(int limit)
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new Reader();
        var result = await new GetRecentTaskExecutions(reader).ExecuteAsync(new(limit), cancellation.Token);
        Assert.True(result.IsSuccess);
        Assert.Same(reader.Items, result.Value);
        Assert.Equal(limit, reader.Limit);
        Assert.Equal(cancellation.Token, reader.Token);
        Assert.Equal(50, new GetRecentTaskExecutionsQuery().Limit);
    }

    private sealed class Reader : IRecentTaskExecutionReader
    {
        internal int? Limit { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal IReadOnlyList<RecentTaskExecution> Items { get; } =
        [new(Guid.NewGuid(), Guid.NewGuid(), "Recorded work", "running", DateTimeOffset.UtcNow, null, 0, 0)];

        public ValueTask<IReadOnlyList<RecentTaskExecution>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
        {
            Limit = limit;
            Token = cancellationToken;
            return ValueTask.FromResult(Items);
        }
    }
}
