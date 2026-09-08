using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations;

/// <inheritdoc />
[DbContext(typeof(ExecutionDbContext))]
[Migration("20260908000000_CompleteStoppedCodexTurns")]
public sealed class CompleteStoppedCodexTurns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE agent_executions AS agent
            SET status = 'completed', ended_at_ticks = terminal.observed_at_ticks
            FROM (
                SELECT agent_execution_id, MAX(observed_at_ticks) AS observed_at_ticks
                FROM observations
                WHERE runtime = 'codex'
                  AND resource_kind IN ('turn-stop', 'subagent-stop')
                  AND agent_execution_id IS NOT NULL
                GROUP BY agent_execution_id
            ) AS terminal
            WHERE agent.id = terminal.agent_execution_id
              AND agent.status = 'running'
              AND terminal.observed_at_ticks >= agent.started_at_ticks;

            UPDATE task_executions AS task
            SET status = 'completed', ended_at_ticks = terminal.observed_at_ticks
            FROM (
                SELECT task_execution_id, MAX(observed_at_ticks) AS observed_at_ticks
                FROM observations
                WHERE runtime = 'codex'
                  AND resource_kind = 'turn-stop'
                GROUP BY task_execution_id
            ) AS terminal
            WHERE task.id = terminal.task_execution_id
              AND task.status = 'running'
              AND terminal.observed_at_ticks >= task.started_at_ticks;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This lifecycle correction is intentionally irreversible: the previous Running state was inaccurate.
    }
}
