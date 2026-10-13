import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-raw-evidence',
  template: `
    <button class="text-button evidence" (click)="toggle()" [attr.aria-expanded]="open()">
      {{ open() ? 'Hide raw evidence' : 'View raw evidence' }}
    </button>
    @if (open()) {
      <div class="raw-evidence">
        @if (loading()) {
          <p role="status">Loading raw evidence…</p>
        }
        @if (error()) {
          <p role="alert">{{ error() }}</p>
          <button (click)="load()">Retry</button>
        }
        @if (content() !== null) {
          <a [href]="url()" download>Download original bytes</a>
          <pre tabindex="0" aria-label="Raw evidence">{{ content() }}</pre>
        }
      </div>
    }
  `,
})
export class RawEvidence {
  readonly observationId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  readonly open = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly content = signal<string | null>(null);
  url() {
    return `/api/observations/${encodeURIComponent(this.observationId())}/evidence`;
  }
  toggle() {
    this.open.update((value) => !value);
    if (this.open() && this.content() === null && !this.loading()) this.load();
  }
  load() {
    this.loading.set(true);
    this.error.set('');
    this.http
      .get(this.url(), { responseType: 'text' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (content) => {
          this.content.set(content);
          this.loading.set(false);
        },
        error: (error) => {
          this.error.set(
            error.status === 404
              ? 'Raw evidence is not available.'
              : 'Could not load raw evidence. Please retry.',
          );
          this.loading.set(false);
        },
      });
  }
}
