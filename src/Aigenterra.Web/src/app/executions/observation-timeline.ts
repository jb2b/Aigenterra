import { Component, computed, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AgentExecution, agentRole, Observation } from './execution-api';

@Component({
  selector: 'app-observation-timeline',
  imports: [DatePipe, RouterLink],
  template: `
    <section class="panel" aria-labelledby="timeline-heading">
      <div class="section-heading">
        <h2 id="timeline-heading">
          Observation timeline <span class="count">{{ observations().length }}</span>
        </h2>
        <span class="muted">{{ visible().length }} shown</span>
      </div>
      <p class="timeline-explanation">
        Ordered by observed time; sequence indicates ingest order. Source order may be unknown.
      </p>
      @if (!observations().length) {
        <p class="panel-empty">No observations have been recorded for this execution.</p>
      }
      <ol class="timeline">
        @for (observation of visible(); track observation.observationId) {
          <li class="observation">
            <div class="observation-heading">
              <span class="kind">{{ observation.kind }}</span
              ><span class="muted mono">Sequence {{ observation.sequence }}</span>
              @if (observation.hasRawEvidence) {
                <span class="evidence">Raw evidence available</span>
              }
            </div>
            <p
              class="observation-summary"
              [class.clamped]="
                isLong(observation.summary) && !expanded().has(observation.observationId)
              "
            >
              {{ observation.summary }}
            </p>
            @if (isLong(observation.summary)) {
              <button
                class="text-button"
                (click)="toggle(observation.observationId)"
                [attr.aria-expanded]="expanded().has(observation.observationId)"
              >
                {{ expanded().has(observation.observationId) ? 'Show less' : 'Show more' }}
              </button>
            }
            <div class="observation-meta">
              <span
                >Observed
                <time [attr.datetime]="observation.observedAt">{{
                  observation.observedAt | date: 'MMM d, y, HH:mm:ss.SSS'
                }}</time></span
              >
              <span
                >Recorded
                <time [attr.datetime]="observation.recordedAt">{{
                  observation.recordedAt | date: 'MMM d, y, HH:mm:ss.SSS'
                }}</time></span
              >
              @if (observation.agentExecutionId; as id) {
                @if (agentLabel(id); as label) {
                  <a [routerLink]="[]" [fragment]="'agent-' + id"
                    >{{ label }} · <span class="mono">…{{ id.slice(-8) }}</span></a
                  >
                } @else {
                  <span
                    >Agent record unavailable · <span class="mono">{{ id }}</span></span
                  >
                }
              } @else {
                <span>No agent associated</span>
              }
            </div>
            <details class="observation-details">
              <summary>Exact metadata</summary>
              <dl class="metadata">
                <dt>Observation ID</dt>
                <dd>
                  <code>{{ observation.observationId }}</code>
                </dd>
                <dt>Observed at</dt>
                <dd>
                  <code>{{ observation.observedAt }}</code>
                </dd>
                <dt>Recorded at</dt>
                <dd>
                  <code>{{ observation.recordedAt }}</code>
                </dd>
                <dt>Agent ID</dt>
                <dd>
                  <code>{{ observation.agentExecutionId ?? 'Not associated' }}</code>
                </dd>
                <dt>Runtime reference</dt>
                <dd>
                  {{ observation.runtimeReference?.runtime ?? 'Not recorded' }} /
                  {{ observation.runtimeReference?.resourceKind ?? '—'
                  }}<code>{{ observation.runtimeReference?.externalId }}</code>
                </dd>
              </dl>
            </details>
          </li>
        }
      </ol>
      @if (visible().length < observations().length) {
        <div class="load-more">
          <button (click)="limit.set(limit() + 100)">Show 100 more</button
          ><span class="muted">{{ observations().length - visible().length }} remaining</span>
        </div>
      }
    </section>
  `,
})
export class ObservationTimeline {
  readonly observations = input.required<Observation[]>();
  readonly agents = input.required<AgentExecution[]>();
  readonly limit = signal(100);
  readonly expanded = signal(new Set<string>());
  readonly visible = computed(() => this.observations().slice(0, this.limit()));
  isLong(summary: string) {
    return summary.length > 240 || summary.includes('\n');
  }
  toggle(id: string) {
    this.expanded.update((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }
  agentLabel(id: string) {
    const agent = this.agents().find((a) => a.agentExecutionId === id);
    return agent ? agentRole(agent) : null;
  }
}
