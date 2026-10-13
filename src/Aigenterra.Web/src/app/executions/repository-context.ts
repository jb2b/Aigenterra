import { Component, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RepositoryContext } from './execution-api';

@Component({
  selector: 'app-repository-context',
  imports: [DatePipe],
  template: `
    <section class="panel" aria-labelledby="repository-heading">
      <div class="section-heading">
        <h2 id="repository-heading">Repository context</h2>
        @if (start()?.headSha && end()?.headSha && start()?.headSha !== end()?.headSha) {
          <span class="badge">HEAD changed</span>
        }
      </div>
      @if (!start() && !end()) {
        <p class="repository-empty">No repository context recorded.</p>
      } @else {
        <div class="repository-snapshots">
          @for (
            entry of [
              { label: 'Start', value: start() },
              { label: 'End', value: end() },
            ];
            track entry.label
          ) {
            <section
              class="repository-snapshot"
              [attr.aria-label]="entry.label + ' repository snapshot'"
            >
              <h3>{{ entry.label }}</h3>
              @if (entry.value; as snapshot) {
                <dl class="repository-metadata">
                  <dt>Capture status</dt>
                  <dd>
                    {{ statusLabel(snapshot.status) }}
                    @if (snapshot.status) {
                      <code>({{ snapshot.status }})</code>
                    }
                  </dd>
                  <dt>Repository root</dt>
                  <dd>
                    <code>{{ snapshot.repositoryRoot || unknown }}</code>
                  </dd>
                  <dt>Worktree path</dt>
                  <dd>
                    <code>{{ snapshot.worktreePath || unknown }}</code>
                  </dd>
                  <dt>Branch</dt>
                  <dd>
                    <code>{{ snapshot.branch || unknown }}</code>
                  </dd>
                  <dt>HEAD commit</dt>
                  <dd>
                    <code class="repository-sha">{{ snapshot.headSha || unknown }}</code>
                    @if (snapshot.headSha; as sha) {
                      <button
                        type="button"
                        class="copy-sha"
                        [attr.aria-label]="'Copy ' + entry.label.toLowerCase() + ' HEAD SHA'"
                        (click)="copySha(sha, entry.label)"
                      >
                        Copy SHA
                      </button>
                    }
                    <span class="copy-feedback" role="status">{{
                      feedback()[entry.label] || ''
                    }}</span>
                  </dd>
                  <dt>Working tree</dt>
                  <dd>
                    {{
                      snapshot.isDirty === true
                        ? 'Dirty'
                        : snapshot.isDirty === false
                          ? 'Clean'
                          : 'Unknown'
                    }}
                  </dd>
                  <dt>Captured at</dt>
                  <dd>
                    {{
                      snapshot.capturedAt ? (snapshot.capturedAt | date: 'medium') : 'Not recorded'
                    }}
                  </dd>
                </dl>
                <details class="repository-exact">
                  <summary>Exact snapshot details</summary>
                  <dl class="repository-metadata">
                    <dt>Captured at (exact)</dt>
                    <dd>
                      <code>{{ snapshot.capturedAt || 'Not recorded' }}</code>
                    </dd>
                    <dt>Shared Git directory</dt>
                    <dd>
                      <code>{{ snapshot.commonGitDirectory || unknown }}</code>
                    </dd>
                  </dl>
                </details>
                <details class="repository-files">
                  <summary>
                    Files — changed: {{ snapshot.changedFiles?.length ?? 'unknown' }}, untracked:
                    {{ snapshot.untrackedFiles?.length ?? 'unknown' }}
                  </summary>
                  <p class="repository-note">
                    Staged and unstaged changes were not recorded separately.
                  </p>
                  @for (
                    group of [
                      { label: 'Changed tracked files', paths: snapshot.changedFiles },
                      { label: 'Untracked files', paths: snapshot.untrackedFiles },
                    ];
                    track group.label
                  ) {
                    <h4>{{ group.label }}</h4>
                    @if (group.paths; as paths) {
                      @if (paths.length) {
                        <ul>
                          @for (path of paths; track $index) {
                            <li>
                              <code>{{ path }}</code>
                            </li>
                          }
                        </ul>
                      } @else {
                        <p>None recorded</p>
                      }
                    } @else {
                      <p>Not recorded</p>
                    }
                  }
                </details>
              } @else {
                <p>No {{ entry.label.toLowerCase() }} snapshot recorded.</p>
              }
            </section>
          }
        </div>
      }
    </section>
  `,
  styles: `
    .repository-empty {
      padding: 20px 24px;
    }
    .repository-snapshots {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
    }
    .repository-snapshot {
      padding: 20px 24px;
      min-width: 0;
    }
    .repository-snapshot + .repository-snapshot {
      border-left: 1px solid #e5eae3;
    }
    .repository-metadata {
      display: grid;
      grid-template-columns: 110px minmax(0, 1fr);
      gap: 10px 16px;
      margin-block: 16px;
      font-size: 14px;
    }
    .repository-sha {
      user-select: text;
    }
    .copy-sha {
      display: block;
      margin-top: 6px;
      font-size: 12px;
      padding: 4px 8px;
    }
    .copy-feedback {
      display: block;
      font-size: 12px;
    }
    .repository-note {
      color: #66746a;
      font-size: 13px;
    }
    .repository-files h4 {
      margin-top: 12px;
      font-size: 14px;
    }
    .repository-files ul {
      padding-left: 20px;
      font-size: 13px;
    }
    .repository-files li {
      margin-top: 6px;
    }
    @media (max-width: 800px) {
      .repository-snapshots {
        grid-template-columns: minmax(0, 1fr);
      }
      .repository-snapshot + .repository-snapshot {
        border-left: 0;
        border-top: 1px solid #e5eae3;
      }
    }
    @media (max-width: 420px) {
      .repository-metadata {
        grid-template-columns: minmax(0, 1fr);
        gap: 4px;
      }
      .repository-metadata dd {
        margin-bottom: 8px;
      }
    }
  `,
})
export class RepositoryContextPanel {
  readonly start = input<RepositoryContext | null>();
  readonly end = input<RepositoryContext | null>();
  readonly unknown = 'Unknown / not recorded';
  readonly feedback = signal<Record<string, string>>({});

  statusLabel(status: string | null | undefined): string {
    const labels: Record<string, string> = {
      captured: 'Captured',
      timeout: 'Timed out',
      git_unavailable: 'Git unavailable',
      not_repository: 'Not a Git repository',
      output_limit: 'Output limit reached',
      git_error: 'Git error',
      changed_during_capture: 'Changed during capture',
      unavailable: 'Unavailable',
      incomplete: 'Incomplete',
    };
    return status ? (labels[status] ?? 'Unrecognized capture status') : 'Unknown / not recorded';
  }

  async copySha(sha: string, label: string) {
    try {
      if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
      await navigator.clipboard.writeText(sha);
      this.feedback.update((value) => ({ ...value, [label]: 'SHA copied.' }));
    } catch {
      this.feedback.update((value) => ({
        ...value,
        [label]: 'Copy unavailable. Select the SHA to copy it manually.',
      }));
    }
  }
}
