using Aigenterra.Application.Evaluations;
using Aigenterra.Domain.Evaluations;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Tests;

public sealed class EvaluationUseCaseTests
{
    [Fact]
    public async Task Missing_execution_does_not_decode_or_save_and_empty_evidence_is_valid()
    {
        var fixture = new Ports();
        var useCase = new EvaluateTaskExecution(fixture, fixture, fixture, TimeProvider.System);
        Assert.False((await useCase.ExecuteAsync(Guid.Empty)).IsSuccess);
        Assert.False((await useCase.ExecuteAsync(Guid.NewGuid())).IsSuccess);
        Assert.Equal(0, fixture.Decodes);
        Assert.Null(fixture.Saved);
        fixture.Input = new(TaskExecutionId.New(), 0, []);
        var result = await useCase.ExecuteAsync(fixture.Input.TaskId.Value);
        Assert.True(result.IsSuccess);
        Assert.All(result.Value!.Evaluation.Findings, f => Assert.Equal(Detection.Unknown, f.Detection));
        fixture.ThrowDecode = true;
        var saved = fixture.Saved;
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(fixture.Input.TaskId.Value).AsTask());
        Assert.Same(saved, fixture.Saved);
    }
    [Fact]
    public async Task Invalid_cursor_and_foreign_evaluation_return_expected_errors()
    {
        var store = new Ports();
        Assert.Equal("invalid_evaluation_request", (await new GetTaskExecutionEvaluations(store).ExecuteAsync(Guid.NewGuid(), "not base64")).Error!.Code);
        Assert.Equal("evaluation_resource_not_found", (await new GetTaskExecutionEvaluation(store).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid())).Error!.Code);
    }
    private sealed class Ports : IExecutionEvaluationInputReader, ICommandEvidenceDecoder, IEvaluationStore
    {
        public EvaluationInput? Input { get; set; }
        public Evaluation? Saved { get; private set; }
        public int Decodes { get; private set; }
        public bool ThrowDecode { get; set; }
        public ValueTask<EvaluationInput?> ReadAsync(TaskExecutionId id, CancellationToken ct) => ValueTask.FromResult(Input);
        public DecodedCommands Decode(EvaluationInput input) { Decodes++; if (ThrowDecode) throw new InvalidOperationException(); return new([], false); }
        public ValueTask<StoredEvaluation> SaveAsync(Evaluation e, CancellationToken ct) { Saved = e; return ValueTask.FromResult(new StoredEvaluation(e, true, false)); }
        public ValueTask<StoredEvaluation?> GetAsync(TaskExecutionId id, Guid evaluationId, CancellationToken ct) => ValueTask.FromResult<StoredEvaluation?>(null);
        public ValueTask<EvaluationPage?> ListAsync(TaskExecutionId id, Guid? before, CancellationToken ct) => ValueTask.FromResult<EvaluationPage?>(null);
    }
}
