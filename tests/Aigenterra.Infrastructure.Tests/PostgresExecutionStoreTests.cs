using System.Data.Common;
using System.Text;
using Aigenterra.Adapters.Codex;
using Aigenterra.Application.Executions;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Aigenterra.Infrastructure.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17.11-bookworm").Build();

    public Task InitializeAsync() => container.StartAsync();
    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    public async Task<string> CreateDatabaseAsync()
    {
        var database = $"test_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
        await command.ExecuteNonQueryAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = database }.ConnectionString;
        await using var db = Context(connectionString);
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        return connectionString;
    }

    internal static ExecutionDbContext Context(string connection, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options);
}

public sealed class PostgresExecutionStoreTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero).AddTicks(3);

    [Fact]
    public async Task Recent_read_is_bounded_stable_and_counts_without_fetching_evidence()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var capture = new QueryCapture();
        await using var db = PostgresFixture.Context(connection, capture);
        var reader = new PostgresRecentTaskExecutionReader(db);
        Assert.Empty(await reader.GetRecentAsync(50));
        var store = new PostgresExecutionStore(db);
        var conversation = new Conversation(ConversationId.New(), Now);
        await store.AddConversationAsync(conversation);
        var low = new TaskExecution(new(Guid.Parse("00000000-0000-0000-0000-000000000001")), conversation.Id, "Lower ID", Now);
        var high = new TaskExecution(new(Guid.Parse("00000000-0000-0000-0000-000000000002")), conversation.Id, "Higher ID", Now, Reference("turn"));
        var newer = new TaskExecution(TaskExecutionId.New(), conversation.Id, "Newer", Now.AddTicks(1));
        foreach (var task in new[] { low, newer, high }) await store.AddTaskExecutionAsync(task);
        var agent = new AgentExecution(AgentExecutionId.New(), high.Id, null, Now);
        await store.AddAgentExecutionAsync(agent);
        await store.AppendObservationAsync(Observation(high.Id, agent.Id), new SourceEvidence("text/plain", new byte[] { 1, 2, 3 }), Now);
        await store.AppendObservationAsync(Observation(high.Id), null, Now);
        capture.Queries.Clear();

        var items = await reader.GetRecentAsync(2);
        Assert.Equal(new[] { newer.Id.Value, high.Id.Value }, items.Select(item => item.TaskExecutionId));
        Assert.Null(items[0].RuntimeReference);
        Assert.Equal(0, items[0].AgentCount);
        Assert.Equal(0, items[0].ObservationCount);
        Assert.Equal(1, items[1].AgentCount);
        Assert.Equal(2, items[1].ObservationCount);
        Assert.Equal(conversation.Id.Value, items[1].ConversationId);
        Assert.Equal("running", items[1].Status);
        Assert.Equal(Now, items[1].StartedAt);
        Assert.Equal(high.RuntimeReference!.ExternalId, items[1].RuntimeReference!.ExternalId);
        Assert.Single(capture.Queries);
        Assert.DoesNotContain("evidence", capture.Queries[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", capture.Queries[0], StringComparison.Ordinal);
        Assert.Equal(low.Id.Value, (await reader.GetRecentAsync(100))[2].TaskExecutionId);
    }

    [Fact]
    public async Task Scopes_round_trip_lifecycle_relations_exact_ticks_and_binary_evidence()
    {
        var connection = await fixture.CreateDatabaseAsync();
        await using var services = new ServiceCollection().AddPostgresExecutions(connection).BuildServiceProvider();
        var conversation = new Conversation(ConversationId.New(), Now, Reference("session"));
        var task = new TaskExecution(TaskExecutionId.New(), conversation.Id, "Persist execution", Now, Reference("turn"));
        var root = new AgentExecution(AgentExecutionId.New(), task.Id, null, Now, Reference("root"));
        var child = new AgentExecution(AgentExecutionId.New(), task.Id, root.Id, Now.AddTicks(1), Reference("child"));
        byte[] original = [0, 255, 13, 10, 32, 128, 1];
        var bytes = original.ToArray();
        var observation = Observation(task.Id, child.Id, Now.AddTicks(2), Reference("event"));
        await using (var scope = services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IExecutionWriteStore>();
            Assert.Same(store, scope.ServiceProvider.GetRequiredService<ITaskExecutionTimelineReader>());
            Assert.Equal(StoreAddResult.Added, await store.AddConversationAsync(conversation));
            Assert.Equal(StoreAddResult.Added, await store.AddTaskExecutionAsync(task));
            Assert.Equal(StoreAddResult.Added, await store.AddAgentExecutionAsync(root));
            Assert.Equal(StoreAddResult.Added, await store.AddAgentExecutionAsync(child));
            var append = store.AppendObservationAsync(observation, new SourceEvidence("application/octet-stream", bytes), Now.AddTicks(7));
            bytes[0] = 42;
            Assert.Equal(StoreAddResult.Added, (await append).Result);
            task.Finish(ExecutionStatus.Completed, Now.AddTicks(8));
            child.Finish(ExecutionStatus.Interrupted, Now.AddTicks(9));
            Assert.True(await store.TryFinishTaskExecutionAsync(task));
            Assert.True(await store.TryFinishAgentExecutionAsync(child));
        }

        await services.MigrateExecutionDevelopmentDatabaseAsync();
        await using (var scope = services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IExecutionWriteStore>();
            Assert.Equal(Now, (await store.FindConversationAsync(conversation.Id))!.StartedAt);
            Assert.Equal(task.EndedAt, (await store.FindTaskExecutionAsync(task.Id))!.EndedAt);
            Assert.Equal(child.EndedAt, (await store.FindAgentExecutionAsync(child.Id))!.EndedAt);
            var lookup = scope.ServiceProvider.GetRequiredService<IExecutionRuntimeReferenceReader>();
            Assert.Equal(conversation.Id, await lookup.FindConversationIdAsync(conversation.RuntimeReference!));
            Assert.Equal(task.Id, await lookup.FindTaskExecutionIdAsync(task.RuntimeReference!));
            Assert.Equal(child.Id, await lookup.FindAgentExecutionIdAsync(child.RuntimeReference!));
            Assert.Equal(observation.Id, await lookup.FindObservationIdAsync(observation.RuntimeReference!));
            var timeline = (await scope.ServiceProvider.GetRequiredService<ITaskExecutionTimelineReader>().GetTimelineAsync(task.Id))!;
            Assert.Equal("completed", timeline.Status);
            Assert.Equal("running", timeline.Agents[0].Status); // No cascade from task finish.
            Assert.Equal(root.Id.Value, timeline.Agents[1].ParentAgentExecutionId);
            Assert.Equal(Now.AddTicks(2), timeline.Observations[0].ObservedAt);
            Assert.Equal(Now.AddTicks(7), timeline.Observations[0].RecordedAt);
            Assert.True(timeline.Observations[0].HasRawEvidence);
            var row = await scope.ServiceProvider.GetRequiredService<ExecutionDbContext>().Observations.AsNoTracking().SingleAsync();
            Assert.Equal(original, row.Evidence);
            Assert.Equal("application/octet-stream", row.MediaType);
        }
    }

    [Fact]
    public async Task Concurrent_reference_registration_has_one_winner_and_loser_contexts_remain_usable()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var reference = Reference("session");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = PostgresFixture.Context(connection);
            var store = new PostgresExecutionStore(db);
            await start.Task;
            var result = await store.AddConversationAsync(new(ConversationId.New(), Now, reference));
            Assert.Empty(db.ChangeTracker.Entries());
            var id = await store.FindConversationIdAsync(reference);
            Assert.NotNull(id);
            Assert.Equal(StoreAddResult.Added, await store.AddConversationAsync(new(ConversationId.New(), Now)));
            return (result, id);
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(attempts);
        Assert.Single(results, item => item.result == StoreAddResult.Added);
        Assert.Equal(11, results.Count(item => item.result == StoreAddResult.DuplicateRuntimeReference));
        Assert.Single(results.Select(item => item.id).Distinct());

        await using var check = PostgresFixture.Context(connection);
        var checkStore = new PostgresExecutionStore(check);
        var winner = results[0].id!.Value;
        Assert.Equal(StoreAddResult.DuplicateId, await checkStore.AddConversationAsync(new(winner, Now)));
        var task = new TaskExecution(TaskExecutionId.New(), winner, "Separate resource namespace", Now, reference);
        Assert.Equal(StoreAddResult.Added, await checkStore.AddTaskExecutionAsync(task));
        Assert.Equal(StoreAddResult.Added, await checkStore.AddConversationAsync(new(ConversationId.New(), Now,
            new RuntimeReference("CODEX", reference.ResourceKind, reference.ExternalId))));
        Assert.Null(await checkStore.FindConversationIdAsync(new("codex", "missing", reference.ExternalId)));
    }

    [Fact]
    public async Task Concurrent_appends_are_monotone_per_task_and_duplicates_or_failures_do_not_consume_sequences()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedTaskAsync(connection);
        var other = await SeedTaskAsync(connection);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = Enumerable.Range(0, 40).Select(async index =>
        {
            await using var db = PostgresFixture.Context(connection);
            var store = new PostgresExecutionStore(db);
            var taskId = index % 2 == 0 ? task.Id : other.Id;
            await start.Task;
            return (taskId, result: await store.AppendObservationAsync(Observation(taskId), null, Now));
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(writes);
        foreach (var group in results.GroupBy(item => item.taskId))
        {
            Assert.All(group, item => Assert.Equal(StoreAddResult.Added, item.result.Result));
            Assert.Equal(Enumerable.Range(1, 20).Select(value => (long)value), group.Select(item => item.result.Receipt!.Sequence).Order());
        }

        await using var context = PostgresFixture.Context(connection);
        var target = new PostgresExecutionStore(context);
        var observation = Observation(task.Id, reference: Reference("duplicate"));
        Assert.Equal(21, (await target.AppendObservationAsync(observation, null, Now)).Receipt!.Sequence);
        Assert.Equal(StoreAddResult.DuplicateId, (await target.AppendObservationAsync(observation, null, Now)).Result);
        Assert.Equal(StoreAddResult.DuplicateRuntimeReference,
            (await target.AppendObservationAsync(Observation(task.Id, reference: observation.RuntimeReference), null, Now)).Result);
        var invalid = Observation(task.Id, AgentExecutionId.New(), reference: Reference("rollback"));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            target.AppendObservationAsync(invalid, new("application/octet-stream", new byte[] { 1 }), Now).AsTask());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)error.InnerException!).SqlState);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(await target.FindObservationIdAsync(invalid.RuntimeReference!));
        Assert.Equal(22, (await target.AppendObservationAsync(Observation(task.Id), null, Now)).Receipt!.Sequence);
        Assert.Equal(22, await context.Tasks.Where(row => row.Id == task.Id.Value).Select(row => row.LastSequence).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_duplicate_codex_deliveries_keep_one_fact_and_original_json_bytes()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var payload = Encoding.UTF8.GetBytes("{ \"hook_event_name\":\"UserPromptSubmit\", \"session_id\":\"session\",\"turn_id\":\"turn\",\"prompt\":\"héllo\" }\r\n");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = PostgresFixture.Context(connection);
            var store = new PostgresExecutionStore(db);
            await start.Task;
            return await Ingestor(store).IngestAsync(payload, Now);
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(writes);
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error?.Code));
        Assert.Single(results, result => result.Value!.Disposition == CodexIngestDisposition.Recorded);
        Assert.Single(results.Select(result => result.Value!.ObservationId).Distinct());
        await using var check = PostgresFixture.Context(connection);
        Assert.Equal(1, await check.Conversations.CountAsync());
        Assert.Equal(1, await check.Tasks.CountAsync());
        Assert.Equal(1, await check.Agents.CountAsync());
        var row = await check.Observations.SingleAsync();
        Assert.Equal(1, row.Sequence);
        Assert.Equal(payload, row.Evidence);
        var again = await Ingestor(new(check)).IngestAsync(payload, Now.AddDays(1));
        Assert.Equal(CodexIngestDisposition.Duplicate, again.Value!.Disposition);
        Assert.Equal(1, await check.Tasks.Select(item => item.LastSequence).SingleAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Stale_finish_cannot_overwrite_a_winner_and_use_cases_return_effective_state(bool agent, bool conflicting)
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedTaskAsync(connection);
        await using var first = PostgresFixture.Context(connection);
        await using var second = PostgresFixture.Context(connection);
        var winner = new PostgresExecutionStore(first);
        var loser = new PostgresExecutionStore(second);
        var loserStatus = conflicting ? ExecutionStatus.Failed : ExecutionStatus.Completed;
        if (agent)
        {
            var original = new AgentExecution(AgentExecutionId.New(), task.Id, null, Now);
            await winner.AddAgentExecutionAsync(original);
            var left = (await winner.FindAgentExecutionAsync(original.Id))!;
            var right = (await loser.FindAgentExecutionAsync(original.Id))!;
            left.Finish(ExecutionStatus.Completed, Now.AddTicks(1));
            right.Finish(loserStatus, Now.AddTicks(2));
            Assert.True(await winner.TryFinishAgentExecutionAsync(left));
            Assert.False(await loser.TryFinishAgentExecutionAsync(right));
            var result = await new FinishAgentExecution(loser, TimeProvider.System).ExecuteAsync(
                new(task.Id.Value, original.Id.Value, loserStatus.ToString(), Now.AddTicks(2)));
            Assert.Equal(!conflicting, result.IsSuccess);
            if (conflicting) { Assert.Equal("conflicting_terminal_status", result.Error!.Code); }
            else { Assert.Equal(Now.AddTicks(1), result.Value!.EndedAt); }
            Assert.Equal(Now.AddTicks(1), (await loser.FindAgentExecutionAsync(original.Id))!.EndedAt);
        }
        else
        {
            var left = (await winner.FindTaskExecutionAsync(task.Id))!;
            var right = (await loser.FindTaskExecutionAsync(task.Id))!;
            left.Finish(ExecutionStatus.Completed, Now.AddTicks(1));
            right.Finish(loserStatus, Now.AddTicks(2));
            Assert.True(await winner.TryFinishTaskExecutionAsync(left));
            Assert.False(await loser.TryFinishTaskExecutionAsync(right));
            var result = await new FinishTaskExecution(loser, TimeProvider.System).ExecuteAsync(
                new(task.Id.Value, loserStatus.ToString(), Now.AddTicks(2)));
            Assert.Equal(!conflicting, result.IsSuccess);
            if (conflicting) { Assert.Equal("conflicting_terminal_status", result.Error!.Code); }
            else { Assert.Equal(Now.AddTicks(1), result.Value!.EndedAt); }
            Assert.Equal(Now.AddTicks(1), (await loser.FindTaskExecutionAsync(task.Id))!.EndedAt);
        }
    }

    [Fact]
    public async Task Timeline_accepts_late_events_orders_by_exact_observed_ticks_and_does_not_select_payloads()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedTaskAsync(connection);
        await using (var write = PostgresFixture.Context(connection))
        {
            var store = new PostgresExecutionStore(write);
            task.Finish(ExecutionStatus.Completed, Now.AddMinutes(1));
            await store.TryFinishTaskExecutionAsync(task);
            await store.AppendObservationAsync(Observation(task.Id, observedAt: Now.AddTicks(1)), new("text/plain", "hello"u8.ToArray()), Now);
            await store.AppendObservationAsync(Observation(task.Id, observedAt: Now), null, Now);
            await store.AppendObservationAsync(Observation(task.Id, observedAt: Now), null, Now);
        }

        var capture = new QueryCapture();
        await using var read = PostgresFixture.Context(connection, capture);
        var timeline = (await new PostgresExecutionStore(read).GetTimelineAsync(task.Id))!;
        Assert.Equal([2L, 3L, 1L], timeline.Observations.Select(item => item.Sequence));
        Assert.Equal([false, false, true], timeline.Observations.Select(item => item.HasRawEvidence));
        Assert.Empty(read.ChangeTracker.Entries());
        Assert.Equal(3, capture.Queries.Count);
        var observationQuery = Assert.Single(capture.Queries, query => query.Contains("FROM observations", StringComparison.Ordinal));
        Assert.Contains("evidence IS NOT NULL", observationQuery);
        Assert.DoesNotContain("media_type", observationQuery);
        Assert.DoesNotContain("conversations", string.Join('\n', capture.Queries));
        Assert.Null(await new PostgresExecutionStore(read).GetTimelineAsync(TaskExecutionId.New()));
    }

    [Fact]
    public async Task Database_rejects_cross_task_parents_and_observation_agents()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedTaskAsync(connection);
        var other = await SeedTaskAsync(connection);
        await using var db = PostgresFixture.Context(connection);
        var store = new PostgresExecutionStore(db);
        var parent = new AgentExecution(AgentExecutionId.New(), other.Id, null, Now);
        await store.AddAgentExecutionAsync(parent);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.AddAgentExecutionAsync(new(AgentExecutionId.New(), task.Id, parent.Id, Now)).AsTask());
        await Assert.ThrowsAsync<DbUpdateException>(() => store.AppendObservationAsync(Observation(task.Id, parent.Id), null, Now).AsTask());
        Assert.Equal(0, await db.Tasks.Where(row => row.Id == task.Id.Value).Select(row => row.LastSequence).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_finish_requests_keep_the_same_first_end_time(bool agent)
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedTaskAsync(connection);
        var participation = new AgentExecution(AgentExecutionId.New(), task.Id, null, Now);
        await using (var seed = PostgresFixture.Context(connection))
        {
            await new PostgresExecutionStore(seed).AddAgentExecutionAsync(participation);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(1, 12).Select(async index =>
        {
            await using var db = PostgresFixture.Context(connection);
            var store = new PostgresExecutionStore(db);
            await start.Task;
            return agent
                ? await new FinishAgentExecution(store, TimeProvider.System).ExecuteAsync(
                    new(task.Id.Value, participation.Id.Value, "completed", Now.AddTicks(index)))
                : await new FinishTaskExecution(store, TimeProvider.System).ExecuteAsync(
                    new(task.Id.Value, "completed", Now.AddTicks(index)));
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(requests);
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error?.Code));
        Assert.Single(results.Select(result => result.Value!.EndedAt).Distinct());
    }

    [Fact]
    public async Task Timeline_queries_share_a_snapshot_while_another_context_commits()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedTaskAsync(connection);
        var interceptor = new BeforeAgentsQuery(async () =>
        {
            await using var writer = PostgresFixture.Context(connection);
            var store = new PostgresExecutionStore(writer);
            await store.AppendObservationAsync(Observation(task.Id), null, Now);
            task.Finish(ExecutionStatus.Completed, Now.AddTicks(1));
            Assert.True(await store.TryFinishTaskExecutionAsync(task));
        });
        await using var reader = PostgresFixture.Context(connection, interceptor);
        var store = new PostgresExecutionStore(reader);
        var before = (await store.GetTimelineAsync(task.Id))!;
        Assert.Equal("running", before.Status);
        Assert.Empty(before.Observations);
        var after = (await store.GetTimelineAsync(task.Id))!;
        Assert.Equal("completed", after.Status);
        Assert.Single(after.Observations);
    }

    private static RuntimeReference Reference(string kind) => new("codex", kind, "external-1");
    private static Observation Observation(TaskExecutionId taskId, AgentExecutionId? agentId = null,
        DateTimeOffset? observedAt = null, RuntimeReference? reference = null) =>
        new(ObservationId.New(), taskId, agentId, observedAt ?? Now, ObservationKind.Message, "Observed", reference);

    private static async Task<TaskExecution> SeedTaskAsync(string connection)
    {
        await using var db = PostgresFixture.Context(connection);
        var store = new PostgresExecutionStore(db);
        var conversation = new Conversation(ConversationId.New(), Now);
        await store.AddConversationAsync(conversation);
        var task = new TaskExecution(TaskExecutionId.New(), conversation.Id, "Test execution", Now);
        await store.AddTaskExecutionAsync(task);
        return task;
    }

    private static CodexHookIngestor Ingestor(PostgresExecutionStore store) => new(
        new(store, TimeProvider.System), new(store, TimeProvider.System), new(store, TimeProvider.System),
        new(store, TimeProvider.System), new(store, TimeProvider.System), new(store, TimeProvider.System), new(store));

    private sealed class QueryCapture : DbCommandInterceptor
    {
        internal List<string> Queries { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Queries.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BeforeAgentsQuery(Func<Task> write) : DbCommandInterceptor
    {
        private bool written;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!written && command.CommandText.Contains("FROM agent_executions", StringComparison.Ordinal))
            {
                written = true;
                await write();
            }

            return result;
        }
    }
}
