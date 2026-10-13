import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
export interface EvaluationSummary {
  evaluationId: string;
  createdAt: string;
  evaluatorId: string;
  version: number;
  category: string;
  watermark: number;
  hasNewerEvidence: boolean;
}
export interface EvaluationAttempt {
  number: number;
  verdict: 'RequestedOnly' | 'Succeeded' | 'Failed' | 'Unknown';
  exitCode: number | null;
  explanation: string;
  observationIds: string[];
}
export interface EvaluationFinding {
  check: string;
  detection: string;
  limitations: string;
  attempts: EvaluationAttempt[];
}
export interface Evaluation extends EvaluationSummary {
  taskExecutionId: string;
  findings: EvaluationFinding[];
}
export interface EvaluationPage {
  items: EvaluationSummary[];
  nextCursor: string | null;
}
@Injectable({ providedIn: 'root' })
export class EvaluationApi {
  private readonly http = inject(HttpClient);
  private url(taskId: string) {
    return `/api/task-executions/${encodeURIComponent(taskId)}/evaluations`;
  }
  list(taskId: string, cursor?: string) {
    return this.http.get<EvaluationPage>(this.url(taskId), { params: cursor ? { cursor } : {} });
  }
  get(taskId: string, evaluationId: string) {
    return this.http.get<Evaluation>(`${this.url(taskId)}/${encodeURIComponent(evaluationId)}`);
  }
  evaluate(taskId: string) {
    return this.http.post<Evaluation>(this.url(taskId), null);
  }
}
