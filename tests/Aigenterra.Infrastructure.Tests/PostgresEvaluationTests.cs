using Aigenterra.Adapters.Codex;
using Aigenterra.Application.Evaluations;
using Aigenterra.Domain.Evaluations;
using Aigenterra.Domain.Executions;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aigenterra.Infrastructure.Tests;

public sealed class PostgresEvaluationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Roundtrip_replay_late_evidence_and_history_are_durable()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedAsync(connection);
        Guid firstId;
        await using (var db = PostgresFixture.Context(connection))
        {
            var store = new PostgresEvaluationStore(db);
            var input = (await store.ReadAsync(task))!;
            Assert.Equal(1, input.Watermark);
            var evaluated = Evaluation(input, 0);
            var first = await store.SaveAsync(evaluated);
            firstId = first.Evaluation.Id;
            Assert.True(first.Created);
            var duplicate = await store.SaveAsync(Evaluation(input, 0));
            Assert.False(duplicate.Created);
            Assert.Equal(firstId, duplicate.Evaluation.Id);
            Assert.Equal(evaluated.Findings[0].Attempts[0].Evidence, duplicate.Evaluation.Findings[0].Attempts[0].Evidence);
            Assert.Null(await store.GetAsync(TaskExecutionId.New(), firstId));
            await new PostgresExecutionStore(db).AppendObservationAsync(new(ObservationId.New(), task, null, DateTimeOffset.UtcNow, ObservationKind.Message, "Late evidence"), null, DateTimeOffset.UtcNow);
        }
        await using (var db = PostgresFixture.Context(connection))
        {
            var store = new PostgresEvaluationStore(db);
            var old = (await store.GetAsync(task, firstId))!;
            Assert.True(old.HasNewerEvidence);
            Assert.Equal(1, old.Evaluation.Watermark);
            var input = (await store.ReadAsync(task))!;
            var newer = await store.SaveAsync(Evaluation(input, 1));
            Assert.NotEqual(firstId, newer.Evaluation.Id);
            Assert.False(newer.HasNewerEvidence);
            Assert.Equal(2, (await store.ListAsync(task, null))!.Items.Count);
            Assert.Equal(CommandVerdict.Succeeded, old.Evaluation.Findings[0].Attempts[0].Verdict);
            Assert.Equal(CommandVerdict.Failed, newer.Evaluation.Findings[0].Attempts[0].Verdict);
        }
    }
    [Fact]
    public async Task Concurrent_evaluations_share_one_identity_and_complete_result()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedAsync(connection);
        EvaluationInput input;
        await using (var db = PostgresFixture.Context(connection)) input = (await new PostgresEvaluationStore(db).ReadAsync(task))!;
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = PostgresFixture.Context(connection);
            return await new PostgresEvaluationStore(db).SaveAsync(Evaluation(input, 0));
        }));
        Assert.Single(results, r => r.Created);
        Assert.Single(results.Select(r => r.Evaluation.Id).Distinct());
        Assert.All(results, r => Assert.Equal(2, r.Evaluation.Findings.Count));
    }
    [Fact]
    public async Task Cross_task_evidence_is_rejected_by_store_and_database()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedAsync(connection);
        var foreign = await SeedAsync(connection);
        await using var db = PostgresFixture.Context(connection);
        var store = new PostgresEvaluationStore(db);
        var input = (await store.ReadAsync(task))!;
        var foreignInput = (await store.ReadAsync(foreign))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Evaluation(input with { Observations = foreignInput.Observations }, 0)).AsTask());
        Assert.Empty((await store.ListAsync(task, null))!.Items);
        var stored = await store.SaveAsync(Evaluation(input, 0));
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE evaluation_evidence SET observation_id = {foreignInput.Observations[0].Id.Value} WHERE evaluation_id = {stored.Evaluation.Id}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM observations WHERE id = {input.Observations[0].Id.Value}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }
    [Fact]
    public async Task Pagination_is_bounded_and_does_not_repeat_items()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedAsync(connection);
        await using var db = PostgresFixture.Context(connection);
        var store = new PostgresEvaluationStore(db);
        var input = (await store.ReadAsync(task))!;
        for (var version = 1; version <= 23; version++)
        {
            var e = Evaluation(input, 0);
            await store.SaveAsync(new(e.Id, task, e.CreatedAt, e.EvaluatorId, version, e.Watermark, e.Findings));
        }
        var first = (await store.ListAsync(task, null))!;
        Assert.Equal(20, first.Items.Count);
        var second = (await store.ListAsync(task, new Guid(Convert.FromBase64String(first.NextCursor!))))!;
        Assert.Equal(3, second.Items.Count);
        Assert.Null(second.NextCursor);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
    }
    [Fact]
    public async Task Migration_preserves_existing_observations_and_empty_evaluation_is_valid()
    {
        var connection = await fixture.CreateDatabaseAsync("20260908000000_CompleteStoppedCodexTurns");
        var task = await SeedAsync(connection);
        await using var db = PostgresFixture.Context(connection);
        await db.Database.MigrateAsync();
        var store = new PostgresEvaluationStore(db);
        var input = (await store.ReadAsync(task))!;
        Assert.Single(input.Observations);
        var useCase = new EvaluateTaskExecution(store, new CodexCommandEvidenceDecoder(), store, TimeProvider.System);
        var result = await useCase.ExecuteAsync(task.Value);
        Assert.True(result.IsSuccess);
        Assert.All(result.Value!.Evaluation.Findings, f => Assert.Equal(Detection.Unknown, f.Detection));
    }
    [Fact]
    public async Task V1_absence_is_preserved_and_v2_reevaluation_is_idempotent_at_each_watermark()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var task = await SeedAsync(connection);
        await using var db = PostgresFixture.Context(connection);
        var store = new PostgresEvaluationStore(db);
        var input = (await store.ReadAsync(task))!;
        var legacy = new Evaluation(Guid.CreateVersion7(), task, DateTimeOffset.UtcNow, BuildTestEvaluator.Id, 1, input.Watermark,
            Enum.GetValues<EvaluationCheck>().Select(check => new EvaluationFinding(check, Detection.NotObserved, "Legacy coverage", [])));
        await store.SaveAsync(legacy);
        var useCase = new EvaluateTaskExecution(store, new CodexCommandEvidenceDecoder(), store, TimeProvider.System);
        var current = (await useCase.ExecuteAsync(task.Value)).Value!;
        Assert.True(current.Created);
        Assert.Equal(2, current.Evaluation.Version);
        Assert.Equal(legacy.Watermark, current.Evaluation.Watermark);
        Assert.NotEqual(legacy.Id, current.Evaluation.Id);
        Assert.All(current.Evaluation.Findings, f => Assert.Equal(Detection.Unknown, f.Detection));
        var duplicate = (await useCase.ExecuteAsync(task.Value)).Value!;
        Assert.False(duplicate.Created);
        Assert.Equal(current.Evaluation.Id, duplicate.Evaluation.Id);
        await new PostgresExecutionStore(db).AppendObservationAsync(new(ObservationId.New(), task, null,
            DateTimeOffset.UtcNow, ObservationKind.Message, "Later evidence"), null, DateTimeOffset.UtcNow);
        var later = (await useCase.ExecuteAsync(task.Value)).Value!;
        Assert.NotEqual(current.Evaluation.Id, later.Evaluation.Id);
        Assert.Equal(input.Watermark + 1, later.Evaluation.Watermark);
        Assert.Equal(3, (await store.ListAsync(task, null))!.Items.Count);
        await using var reopened = PostgresFixture.Context(connection);
        var old = (await new PostgresEvaluationStore(reopened).GetAsync(task, legacy.Id))!.Evaluation;
        Assert.Equal(1, old.Version);
        Assert.All(old.Findings, f => { Assert.Equal(Detection.NotObserved, f.Detection); Assert.Equal("Legacy coverage", f.Limitations); });
    }
    private static async Task<TaskExecutionId> SeedAsync(string connection)
    {
        await using var db = PostgresFixture.Context(connection);
        var store = new PostgresExecutionStore(db);
        var conversation = new Conversation(ConversationId.New(), DateTimeOffset.UtcNow);
        var task = new TaskExecution(TaskExecutionId.New(), conversation.Id, "Evaluate recorded work", DateTimeOffset.UtcNow);
        await store.AddConversationAsync(conversation);
        await store.AddTaskExecutionAsync(task);
        await store.AppendObservationAsync(new(ObservationId.New(), task.Id, null, DateTimeOffset.UtcNow, ObservationKind.Message, "Source"), null, DateTimeOffset.UtcNow);
        return task.Id;
    }
    private static Evaluation Evaluation(EvaluationInput input, int exit) => new(Guid.CreateVersion7(), input.TaskId, DateTimeOffset.UtcNow, BuildTestEvaluator.Id, 1, input.Watermark,
        [new(EvaluationCheck.Build, Detection.Observed, "Command result only", [new(1, exit == 0 ? CommandVerdict.Succeeded : CommandVerdict.Failed, exit, "Runtime exit code", [input.Observations[0].Id])]),
         new(EvaluationCheck.Tests, Detection.NotObserved, "Command result only", [])]);
}
