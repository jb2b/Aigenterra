import { Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AgentExecution, agentRole } from './execution-api';

@Component({
  selector: 'app-agent-list',
  imports: [DatePipe, RouterLink],
  template: `
    <section class="panel" aria-labelledby="agents-heading">
      <div class="section-heading">
        <h2 id="agents-heading">
          Agents <span class="count">{{ agents().length }}</span>
        </h2>
        <span class="muted">Known relationships only</span>
      </div>
      @if (!agents().length) {
        <p class="panel-empty">No agents have been recorded for this execution.</p>
      }
      <ul class="agent-list">
        @for (agent of agents(); track agent.agentExecutionId) {
          <li [id]="'agent-' + agent.agentExecutionId" class="agent-row">
            <div>
              <div class="inline-meta">
                <strong>{{ role(agent) }}</strong
                ><span class="badge" [attr.data-status]="agent.status">{{ agent.status }}</span>
              </div>
              <code>{{ agent.agentExecutionId }}</code>
            </div>
            <div class="agent-lineage">
              @if (agent.parentAgentExecutionId; as parent) {
                <span class="muted">Parent</span>
                @if (hasAgent(parent)) {
                  <a [routerLink]="[]" [fragment]="'agent-' + parent" class="mono">{{ parent }}</a>
                } @else {
                  <code>{{ parent }}</code
                  ><span class="muted">Parent record unavailable</span>
                }
              } @else if (role(agent) === 'Subagent') {
                <span class="muted">Parent unknown</span>
              } @else if (role(agent) !== 'Root agent') {
                <span class="muted">Lineage unknown</span>
              } @else {
                <span class="muted">Identified by runtime metadata</span>
              }
            </div>
            <details class="agent-details">
              <summary>Agent metadata</summary>
              <dl class="metadata">
                <dt>Started</dt>
                <dd>
                  {{ agent.startedAt | date: 'medium' }}<code>{{ agent.startedAt }}</code>
                </dd>
                <dt>Ended</dt>
                <dd>{{ agent.endedAt ?? 'Not recorded' }}</dd>
                <dt>Runtime reference</dt>
                <dd>
                  {{ agent.runtimeReference?.runtime ?? 'Not recorded' }} /
                  {{ agent.runtimeReference?.resourceKind ?? '—'
                  }}<code>{{ agent.runtimeReference?.externalId }}</code>
                </dd>
              </dl>
            </details>
          </li>
        }
      </ul>
    </section>
  `,
})
export class AgentList {
  readonly agents = input.required<AgentExecution[]>();
  readonly role = agentRole;
  hasAgent(id: string) {
    return this.agents().some((agent) => agent.agentExecutionId === id);
  }
}
