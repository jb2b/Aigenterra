import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';

// HTTP contracts owned by Api. These are not Domain models.
export interface RuntimeReference {
  runtime: string;
  resourceKind: string;
  externalId: string;
}
export interface RecentExecution {
  taskExecutionId: string;
  conversationId: string;
  objective: string;
  status: string;
  startedAt: string;
  runtimeReference: RuntimeReference | null;
  agentCount: number;
  observationCount: number;
}
export interface AgentExecution {
  agentExecutionId: string;
  parentAgentExecutionId: string | null;
  status: string;
  startedAt: string;
  endedAt: string | null;
  runtimeReference: RuntimeReference | null;
}
export interface Observation {
  observationId: string;
  agentExecutionId: string | null;
  observedAt: string;
  recordedAt: string;
  sequence: number;
  kind: string;
  summary: string;
  runtimeReference: RuntimeReference | null;
  hasRawEvidence: boolean;
}
export interface ExecutionTimeline {
  taskExecutionId: string;
  conversationId: string;
  objective: string;
  status: string;
  startedAt: string;
  endedAt: string | null;
  runtimeReference: RuntimeReference | null;
  agents: AgentExecution[];
  observations: Observation[];
}
@Injectable({ providedIn: 'root' })
export class ExecutionApi {
  private readonly http = inject(HttpClient);
  recent() {
    return this.http.get<{ items: RecentExecution[] }>('/api/task-executions', {
      params: { limit: 50 },
    });
  }
  timeline(id: string) {
    return this.http.get<ExecutionTimeline>(
      `/api/task-executions/${encodeURIComponent(id)}/timeline`,
    );
  }
}
export function requestError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 404)
      return 'Execution not found. It may not have been recorded in this database.';
    if ([0, 502, 503, 504].includes(error.status))
      return 'API unavailable. Check that Aigenterra is running, then retry.';
    if (error.status === 400)
      return 'This execution request is invalid. Return to the execution list.';
  }
  return 'The server could not load this data. Please retry.';
}
// Presentation of explicit runtime metadata; never infer missing parent relationships.
export function agentRole(agent: AgentExecution): string {
  const ref = agent.runtimeReference;
  if (ref?.runtime === 'codex' && ref.resourceKind === 'turn-root-agent') return 'Root agent';
  if (ref?.runtime === 'codex' && ref.resourceKind === 'subagent') return 'Subagent';
  return agent.parentAgentExecutionId ? 'Subagent' : 'Agent';
}
export const localTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
