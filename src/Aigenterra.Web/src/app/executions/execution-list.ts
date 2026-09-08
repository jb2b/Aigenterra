import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ExecutionApi, localTimeZone, RecentExecution, requestError } from './execution-api';

@Component({
  imports: [DatePipe, RouterLink],
  template: `
    <div class="page-heading">
      <div>
        <p class="eyebrow">Flight recorder</p>
        <h1>Executions</h1>
        <p class="lede">Explore recorded prompt turns across agent conversations.</p>
      </div>
      <button class="primary" (click)="load()" [disabled]="loading()">
        {{ loading() ? 'Refreshing…' : 'Refresh' }}
      </button>
    </div>
    @if (error()) {
      <div class="notice error" role="alert">
        <p>
          {{ error() }}
          @if (items() !== null) {
            Previously loaded data is shown.
          }
        </p>
        <button (click)="load()" [disabled]="loading()">Retry</button>
      </div>
    }
    <section class="panel" aria-label="Recent executions" [attr.aria-busy]="loading()">
      <div class="section-heading">
        <h2>Recent executions</h2>
        <span class="muted">{{ items()?.length ?? 0 }} shown · latest 50</span>
      </div>
      @if (loading() && items() === null) {
        <div class="state" role="status">
          <h3>Loading executions…</h3>
          <p>Reading your recorded activity.</p>
        </div>
      }
      @if (items()?.length === 0) {
        <div class="state">
          <span class="empty-mark" aria-hidden="true">↗</span>
          <h3>No executions recorded yet</h3>
          <p>Run a Codex task with the repository hooks enabled, then refresh this page.</p>
        </div>
      }
      @if (items(); as executions) {
        <ul class="execution-list">
          @for (execution of executions; track execution.taskExecutionId) {
            <li class="execution-row">
              <div class="execution-objective">
                <a
                  class="objective-link"
                  [routerLink]="['/executions', execution.taskExecutionId]"
                  >{{ execution.objective }}</a
                ><span class="muted mono">{{ execution.taskExecutionId }}</span>
              </div>
              <div class="execution-meta">
                <span class="badge" [attr.data-status]="execution.status">{{
                  execution.status
                }}</span
                ><span class="runtime">{{
                  execution.runtimeReference?.runtime ?? 'Unknown runtime'
                }}</span>
              </div>
              <div class="execution-counts">
                <span
                  >{{ execution.agentCount }}
                  {{ execution.agentCount === 1 ? 'agent' : 'agents' }}</span
                ><span
                  >{{ execution.observationCount }}
                  {{ execution.observationCount === 1 ? 'observation' : 'observations' }}</span
                >
              </div>
              <div class="execution-time">
                <time [attr.datetime]="execution.startedAt"
                  >{{ execution.startedAt | date: 'MMM d, y' }}<br />{{
                    execution.startedAt | date: 'HH:mm:ss'
                  }}</time
                >
              </div>
            </li>
          }
        </ul>
      }
    </section>
    <div class="page-note">
      <span>Times shown in {{ timeZone }}.</span><span>New recordings appear after refresh.</span>
    </div>
  `,
})
export class ExecutionList {
  private readonly api = inject(ExecutionApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly items = signal<RecentExecution[] | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly timeZone = localTimeZone;
  constructor() {
    this.load();
  }
  load() {
    if (this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    this.api
      .recent()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.items.set(response.items);
          this.loading.set(false);
        },
        error: (error) => {
          this.error.set(requestError(error));
          this.loading.set(false);
        },
      });
  }
}
