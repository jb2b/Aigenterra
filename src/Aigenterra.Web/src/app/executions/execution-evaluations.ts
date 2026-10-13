import { Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Subscription } from 'rxjs';
import { Evaluation, EvaluationApi, EvaluationFinding, EvaluationSummary } from './evaluation-api';

@Component({
  selector: 'app-execution-evaluations',
  imports: [DatePipe],
  template: `
    <section class="panel evaluation-panel" aria-labelledby="evaluations-heading">
      <div class="section-heading">
        <h2 id="evaluations-heading">Evaluations</h2>
        <button class="primary" (click)="evaluate()" [disabled]="evaluating() || loading()">
          {{ evaluating() ? 'Evaluating…' : selected() ? 'Re-evaluate' : 'Evaluate' }}
        </button>
      </div>
      <p class="muted">Deterministic command results, not a claim that test cases ran or passed.</p>
      @if (error()) {
        <div class="notice error" role="alert">
          {{ error() }} Previously loaded results remain visible.
          <button (click)="load()" [disabled]="loading()">Retry loading</button>
        </div>
      }
      @if (loading()) {
        <p role="status">Loading evaluations…</p>
      }
      @if (!loading() && !items().length && !selected()) {
        <p>No evaluation recorded. Evaluate the available recorded evidence.</p>
      }
      @if (items().length) {
        <details>
          <summary>Evaluation history ({{ items().length }} loaded)</summary>
          <ul class="evaluation-history">
            @for (item of items(); track item.evaluationId) {
              <li>
                <button
                  (click)="select(item.evaluationId)"
                  [disabled]="loading() || evaluating()"
                  [attr.aria-pressed]="selected()?.evaluationId === item.evaluationId"
                >
                  {{ item.createdAt | date: 'medium' }} · v{{ item.version }} · through #{{
                    item.watermark
                  }}
                </button>
              </li>
            }
          </ul>
          @if (cursor()) {
            <button (click)="load(true)" [disabled]="loading()">More evaluations</button>
          }
        </details>
      }
      @if (selected(); as result) {
        <p class="inline-meta">
          <span class="badge">{{ result.category }}</span> {{ result.evaluatorId }} v{{
            result.version
          }}
          · evidence through #{{ result.watermark }}
        </p>
        @if (result.hasNewerEvidence || sequence() > result.watermark) {
          <p class="notice" role="status">
            Newer evidence is available. Re-evaluate to create a new snapshot.
          </p>
        }
        @for (finding of result.findings; track finding.check) {
          <section
            class="evaluation-finding"
            [attr.aria-label]="finding.check + ' command results'"
          >
            <h3>{{ finding.check }}</h3>
            <p>{{ summary(finding) }}</p>
            <p>{{ explanation(finding) }}</p>
            @if (finding.attempts.length) {
              <p>{{ counts(finding) }}</p>
            }
            <p class="muted">{{ finding.limitations }}</p>
            <ol>
              @for (attempt of finding.attempts; track attempt.number) {
                <li>
                  <p>
                    <strong>{{ attempt.verdict }}</strong>
                    @if (attempt.exitCode !== null) {
                      · exit {{ attempt.exitCode }}
                    }
                    — {{ attempt.explanation }}
                  </p>
                  <div class="evidence-links" aria-label="Supporting observations">
                    @for (id of attempt.observationIds; track id; let number = $index) {
                      <button class="text-button" (click)="evidenceSelected.emit(id)">
                        Evidence {{ number + 1 }} · …{{ id.slice(-8) }}
                      </button>
                    }
                  </div>
                </li>
              }
            </ol>
          </section>
        }
      }
    </section>
  `,
})
export class ExecutionEvaluations {
  readonly taskId = input.required<string>();
  readonly sequence = input(0);
  readonly evidenceSelected = output<string>();
  readonly items = signal<EvaluationSummary[]>([]);
  readonly selected = signal<Evaluation | null>(null);
  readonly cursor = signal<string | null>(null);
  readonly loading = signal(false);
  readonly evaluating = signal(false);
  readonly error = signal('');
  private readonly api = inject(EvaluationApi);
  private historyRequest?: Subscription;
  private detailRequest?: Subscription;
  private evaluationRequest?: Subscription;
  constructor() {
    inject(DestroyRef).onDestroy(() => this.cancel());
    effect(() => {
      const id = this.taskId();
      this.cancel();
      this.items.set([]);
      this.selected.set(null);
      this.cursor.set(null);
      this.load(false, id);
    });
  }
  private cancel() {
    this.historyRequest?.unsubscribe();
    this.detailRequest?.unsubscribe();
    this.evaluationRequest?.unsubscribe();
    this.evaluating.set(false);
  }
  load(more = false, id = this.taskId()) {
    this.historyRequest?.unsubscribe();
    this.loading.set(true);
    this.error.set('');
    this.historyRequest = this.api
      .list(id, more ? (this.cursor() ?? undefined) : undefined)
      .subscribe({
        next: (page) => {
          this.items.update((old) => (more ? [...old, ...page.items] : page.items));
          this.cursor.set(page.nextCursor);
          this.loading.set(false);
          if (!this.selected() && page.items.length) this.select(page.items[0].evaluationId);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Could not load evaluations.');
        },
      });
  }
  select(id: string) {
    this.detailRequest?.unsubscribe();
    this.loading.set(true);
    this.error.set('');
    this.detailRequest = this.api.get(this.taskId(), id).subscribe({
      next: (result) => {
        this.selected.set(result);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Could not load this evaluation.');
      },
    });
  }
  evaluate() {
    if (this.evaluating() || this.loading()) return;
    this.error.set('');
    this.evaluating.set(true);
    this.evaluationRequest = this.api.evaluate(this.taskId()).subscribe({
      next: (result) => {
        this.selected.set(result);
        this.evaluating.set(false);
        this.load();
      },
      error: () => {
        this.evaluating.set(false);
        this.error.set('Evaluation could not be completed. Retry with Evaluate.');
      },
    });
  }
  summary(finding: EvaluationFinding) {
    if (!finding.attempts.length)
      return finding.detection === 'NotObserved'
        ? 'Not observed'
        : 'Unknown — insufficient evidence';
    if (finding.attempts.every((a) => a.verdict === 'Succeeded')) return 'Observed — success';
    if (finding.attempts.every((a) => a.verdict === 'Failed')) return 'Observed — failure';
    if (finding.attempts.every((a) => a.verdict === 'RequestedOnly' || a.verdict === 'Unknown'))
      return 'Observed — outcome unknown';
    return 'Observed — mixed or incomplete outcomes';
  }
  explanation(finding: EvaluationFinding) {
    if (!finding.attempts.length)
      return finding.detection === 'NotObserved'
        ? `No supported ${finding.check === 'Build' ? 'build' : 'test'} command was found in the interpretable recorded evidence.`
        : 'Recorded evidence is insufficient to determine whether a supported command was attempted.';
    if (finding.attempts.every((a) => a.verdict === 'RequestedOnly'))
      return 'A supported command was requested; execution and outcome are not established.';
    if (finding.attempts.every((a) => a.verdict === 'RequestedOnly' || a.verdict === 'Unknown'))
      return 'Supported command activity was recorded; no reliable terminal outcome is available.';
    return 'Explicit terminal results apply to individual commands, not to whether test cases ran or passed.';
  }
  counts(finding: EvaluationFinding) {
    const count = (verdict: string) => finding.attempts.filter((a) => a.verdict === verdict).length;
    return `${finding.attempts.length} attempts · ${count('Succeeded')} succeeded · ${count('Failed')} failed · ${count('RequestedOnly')} requested only · ${count('Unknown')} unknown`;
  }
}
