import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RawEvidence } from './raw-evidence';
import { promptSnippet } from './prompt-snippet';

describe('Raw evidence inspection', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  it('loads only on click, renders source as text and preserves it across collapse', () => {
    const fixture = TestBed.createComponent(RawEvidence);
    fixture.componentRef.setInput('observationId', 'source');
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectNone('/api/observations/source/evidence');
    fixture.nativeElement.querySelector('button').click();
    const raw = '{ "output": "<img src=x onerror=alert(1)> héllo" }\r\n';
    http.expectOne('/api/observations/source/evidence').flush(raw);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('pre').textContent).toBe(raw);
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe(
      '/api/observations/source/evidence',
    );
    fixture.nativeElement.querySelector('button').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('pre')).toBeNull();
    fixture.nativeElement.querySelector('button').click();
    fixture.detectChanges();
    http.expectNone('/api/observations/source/evidence');
    expect(fixture.nativeElement.querySelector('pre').textContent).toBe(raw);
  });
  it('allows retry after a failed evidence request', () => {
    const fixture = TestBed.createComponent(RawEvidence);
    fixture.componentRef.setInput('observationId', 'source');
    fixture.detectChanges();
    fixture.componentInstance.toggle();
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/observations/source/evidence')
      .flush('', { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=alert]')).not.toBeNull();
    fixture.nativeElement.querySelector('.raw-evidence button').click();
    http.expectOne('/api/observations/source/evidence').flush('{}');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('pre').textContent).toBe('{}');
  });
  it('makes bounded single-line snippets without changing the original prompt', () => {
    const prompt = 'Plan\r\n' + 'step '.repeat(100);
    expect(promptSnippet(prompt).length).toBeLessThanOrEqual(181);
    expect(promptSnippet(prompt)).not.toContain('\n');
    expect(promptSnippet(prompt).endsWith('…')).toBe(true);
    expect(promptSnippet('Short prompt')).toBe('Short prompt');
    expect(prompt).toContain('\r\n');
  });
});
