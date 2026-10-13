import { promptSnippet } from './prompt-snippet';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { ExecutionApi, ExecutionTimeline, localTimeZone, requestError } from './execution-api';
import { AgentList } from './agent-list';
import { ObservationTimeline } from './observation-timeline';
import { ExecutionEvaluations } from './execution-evaluations';
import { RepositoryContextPanel } from './repository-context';

@Component({
  imports: [
    DatePipe,
    RouterLink,
    AgentList,
    ObservationTimeline,
    ExecutionEvaluations,
    RepositoryContextPanel,
  ],
  template: `
    <a class="back-link" routerLink="/executions">← All executions</a>
    <div class="page-heading">
      <div>
        <p class="eyebrow">Execution detail</p>
        <h1>Recorded execution</h1>
      </div>
      <button class="primary" (click)="load()" [disabled]="loading()">
        {{ loading() ? 'Refreshing…' : 'Refresh' }}
      </button>
    </div>
    @if (error()) {
      <div class="notice error" role="alert">
        <p>
          {{ error() }}
          @if (execution()) {
            Previously loaded data is shown.
          }
        </p>
        <button (click)="load()" [disabled]="loading()">Retry</button>
      </div>
    }
    @if (loading() && !execution()) {
      <div class="panel state" role="status">
        <h2>Loading execution…</h2>
        <p>Reading recorded agents and observations.</p>
      </div>
    }
    @if (execution(); as task) {
      <nav class="section-links" aria-label="Execution sections">
        <a [routerLink]="[]" fragment="repository-heading">Repository context</a>
        <a [routerLink]="[]" fragment="agents-heading">Agents ({{ task.agents.length }})</a>
        <a [routerLink]="[]" fragment="evaluations-heading">Evaluations</a>
        <a [routerLink]="[]" fragment="timeline-heading"
          >Observation timeline ({{ task.observations.length }})</a
        >
      </nav>
      <section
        class="panel execution-summary"
        aria-label="Execution metadata"
        [attr.aria-busy]="loading()"
      >
        <div class="inline-meta">
          <span class="badge" [attr.data-status]="task.status">{{ task.status }}</span
          ><span class="runtime">{{ task.runtimeReference?.runtime ?? 'Unknown runtime' }}</span>
        </div>
        <h2 class="full-objective">{{ snippet(task.objective) }}</h2>
        @if (snippet(task.objective) !== task.objective) {
          <details class="prompt-details">
            <summary>Show full prompt</summary>
            <pre class="full-prompt">{{ task.objective }}</pre>
          </details>
        }
        <p class="status-explanation">
          Each Codex execution represents one prompt turn within its conversation.
        </p>
        <dl class="task-metadata">
          <div>
            <dt>Started</dt>
            <dd>{{ task.startedAt | date: 'medium' }}</dd>
          </div>
          <div>
            <dt>Ended</dt>
            <dd>{{ task.endedAt ? (task.endedAt | date: 'medium') : 'Not recorded' }}</dd>
          </div>
          <div>
            <dt>Execution ID</dt>
            <dd>
              <code>{{ task.taskExecutionId }}</code>
            </dd>
          </div>
          <div>
            <dt>Conversation ID</dt>
            <dd>
              <code>{{ task.conversationId }}</code>
            </dd>
          </div>
        </dl>
        <details>
          <summary>Exact timestamps & runtime reference</summary>
          <dl class="metadata">
            <dt>Started at</dt>
            <dd>
              <code>{{ task.startedAt }}</code>
            </dd>
            <dt>Ended at</dt>
            <dd>
              <code>{{ task.endedAt ?? 'Not recorded' }}</code>
            </dd>
            <dt>Runtime / kind</dt>
            <dd>
              {{ task.runtimeReference?.runtime ?? 'Not recorded' }} /
              {{ task.runtimeReference?.resourceKind ?? '—' }}
            </dd>
            <dt>External ID</dt>
            <dd>
              <code>{{ task.runtimeReference?.externalId ?? 'Not recorded' }}</code>
            </dd>
          </dl>
        </details>
      </section>
      <div class="page-note">
        <span>Times shown in {{ timeZone }}.</span
        ><span>Refresh to retrieve new observations.</span>
      </div>
      <div class="detail-stack">
        <app-repository-context [start]="task.startContext" [end]="task.endContext" />
        <app-agent-list [agents]="task.agents" />
        <app-execution-evaluations
          [taskId]="task.taskExecutionId"
          [sequence]="watermark(task)"
          (evidenceSelected)="timeline.reveal($event)"
        />
        <app-observation-timeline
          #timeline
          [observations]="task.observations"
          [agents]="task.agents"
        />
      </div>
    }
  `,
})
export class ExecutionDetail {
  private readonly api = inject(ExecutionApi);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  private id = '';
  readonly execution = signal<ExecutionTimeline | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly timeZone = localTimeZone;
  readonly snippet = promptSnippet;
  watermark(task: ExecutionTimeline) {
    return task.observations.reduce((highest, item) => Math.max(highest, item.sequence), 0);
  }
  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.id = params.get('id') ?? '';
      this.execution.set(null);
      this.load();
    });
  }
  load() {
    this.request?.unsubscribe();
    this.error.set('');
    if (!/^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(this.id)) {
      this.error.set('Execution not found. Open an execution from the list.');
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.request = this.api
      .timeline(this.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (task) => {
          this.execution.set(task);
          this.loading.set(false);
        },
        error: (error) => {
          this.error.set(requestError(error));
          this.loading.set(false);
        },
      });
  }
}
