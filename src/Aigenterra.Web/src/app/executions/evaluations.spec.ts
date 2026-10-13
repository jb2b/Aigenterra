import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ExecutionEvaluations } from './execution-evaluations';
import { Evaluation, EvaluationAttempt } from './evaluation-api';
import { ObservationTimeline } from './observation-timeline';
import { provideRouter } from '@angular/router';

const base = '/api/task-executions/task/evaluations';
const result: Evaluation = {
  evaluationId: 'evaluation',
  taskExecutionId: 'task',
  createdAt: '2026-09-09T12:00:00Z',
  evaluatorId: 'dotnet-command-results',
  version: 1,
  category: 'Deterministic',
  watermark: 3,
  hasNewerEvidence: false,
  findings: [
    {
      check: 'Build',
      detection: 'Observed',
      limitations: 'Command results only',
      attempts: [
        {
          number: 1,
          verdict: 'Failed',
          exitCode: 1,
          explanation: 'Non-zero exit',
          observationIds: ['source-1'],
        },
        {
          number: 2,
          verdict: 'Succeeded',
          exitCode: 0,
          explanation: 'Command completed',
          observationIds: ['source-2'],
        },
      ],
    },
    { check: 'Tests', detection: 'Unknown', limitations: 'Missing evidence', attempts: [] },
  ],
};
describe('Evidence-backed evaluations', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it.each([
    {
      verdicts: ['RequestedOnly'],
      label: 'Observed — outcome unknown',
      explanation: 'execution and outcome are not established',
    },
    {
      verdicts: ['Unknown'],
      label: 'Observed — outcome unknown',
      explanation: 'no reliable terminal outcome is available',
    },
    {
      verdicts: ['RequestedOnly', 'Unknown'],
      label: 'Observed — outcome unknown',
      explanation: 'no reliable terminal outcome is available',
    },
    { verdicts: ['Succeeded'], label: 'Observed — success', explanation: 'individual commands' },
    { verdicts: ['Failed'], label: 'Observed — failure', explanation: 'individual commands' },
    {
      verdicts: ['Succeeded', 'Unknown'],
      label: 'Observed — mixed or incomplete outcomes',
      explanation: 'individual commands',
    },
    {
      verdicts: ['Succeeded', 'Failed'],
      label: 'Observed — mixed or incomplete outcomes',
      explanation: 'individual commands',
    },
  ])(
    'renders $label for $verdicts independently of the other check',
    async ({ verdicts, label, explanation }) => {
      const fixture = TestBed.createComponent(ExecutionEvaluations);
      fixture.componentRef.setInput('taskId', 'task');
      fixture.detectChanges();
      const current: Evaluation = {
        ...result,
        version: 2,
        findings: [
          {
            check: 'Build',
            detection: 'Observed',
            limitations: 'Recorded evidence only',
            attempts: verdicts.map((verdict, index) => ({
              number: index + 1,
              verdict: verdict as EvaluationAttempt['verdict'],
              exitCode: verdict === 'Succeeded' ? 0 : verdict === 'Failed' ? 1 : null,
              explanation: 'Specific recorded explanation',
              observationIds: ['source-' + index],
            })),
          },
          {
            check: 'Tests',
            detection: 'NotObserved',
            limitations: 'Recorded evidence only',
            attempts: [],
          },
        ],
      };
      http.expectOne(base).flush({ items: [current], nextCursor: null });
      http.expectOne(base + '/evaluation').flush(current);
      await fixture.whenStable();
      const sections = fixture.nativeElement.querySelectorAll('.evaluation-finding');
      expect(sections[0].textContent).toContain(label);
      expect(sections[0].textContent).toContain(explanation);
      expect(sections[0].textContent).toContain(`${verdicts.length} attempts`);
      expect(sections[0].textContent).toContain('Specific recorded explanation');
      expect(sections[1].textContent).toContain('Not observed');
      expect(sections[1].textContent).toContain('No supported test command');
      expect(fixture.nativeElement.textContent).toContain('v2');
    },
  );
  it('evaluates explicitly, retains all attempts, flags stale snapshots and links evidence', async () => {
    const fixture = TestBed.createComponent(ExecutionEvaluations);
    fixture.componentRef.setInput('taskId', 'task');
    fixture.detectChanges();
    http.expectOne(base).flush({ items: [], nextCursor: null });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('No evaluation recorded');
    fixture.componentInstance.evaluate();
    const request = http.expectOne(base);
    expect(request.request.method).toBe('POST');
    request.flush(result);
    http.expectOne(base).flush({ items: [result], nextCursor: null });
    fixture.componentRef.setInput('sequence', 4);
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('2 attempts · 1 succeeded · 1 failed');
    expect(text).toContain('Unknown — insufficient evidence');
    expect(text).toContain('Observed — mixed or incomplete outcomes');
    expect(text).toContain('Newer evidence is available');
    expect(text).toContain('not a claim that test cases ran or passed');
    const selected: string[] = [];
    fixture.componentInstance.evidenceSelected.subscribe((id) => selected.push(id));
    fixture.nativeElement.querySelector('.evidence-links button').click();
    expect(selected).toEqual(['source-1']);
    fixture.componentInstance.evaluate();
    http.expectOne(base).flush({}, { status: 500, statusText: 'Failure' });
    await fixture.whenStable();
    expect(fixture.componentInstance.selected()?.evaluationId).toBe('evaluation');
    expect(fixture.nativeElement.textContent).toContain('Evaluation could not be completed');
  });
  it('loads latest history without evaluating, pages and selects an older result', async () => {
    const fixture = TestBed.createComponent(ExecutionEvaluations);
    fixture.componentRef.setInput('taskId', 'task');
    fixture.detectChanges();
    http.expectOne(base).flush({ items: [result], nextCursor: 'next' });
    http.expectOne(base + '/evaluation').flush(result);
    fixture.componentInstance.load(true);
    http
      .expectOne(base + '?cursor=next')
      .flush({ items: [{ ...result, evaluationId: 'old' }], nextCursor: null });
    fixture.componentInstance.select('old');
    http.expectOne(base + '/old').flush({
      ...result,
      evaluationId: 'old',
      findings: [
        {
          check: 'Build',
          detection: 'NotObserved',
          limitations: 'Recorded evidence only',
          attempts: [],
        },
      ],
    });
    await fixture.whenStable();
    expect(fixture.componentInstance.items().length).toBe(2);
    expect(fixture.nativeElement.textContent).toContain(
      'No supported build command was found in the interpretable recorded evidence',
    );
  });
  it('cancels old requests on execution navigation', async () => {
    const fixture = TestBed.createComponent(ExecutionEvaluations);
    fixture.componentRef.setInput('taskId', 'task');
    fixture.detectChanges();
    const old = http.expectOne(base);
    fixture.componentRef.setInput('taskId', 'other');
    fixture.detectChanges();
    expect(old.cancelled).toBe(true);
    http.expectOne('/api/task-executions/other/evaluations').flush({ items: [], nextCursor: null });
    await fixture.whenStable();
    expect(fixture.componentInstance.selected()).toBeNull();
  });
  it('reveals and highlights evidence after the first 100 observations', async () => {
    const fixture = TestBed.createComponent(ObservationTimeline);
    fixture.componentRef.setInput('agents', []);
    fixture.componentRef.setInput(
      'observations',
      Array.from({ length: 150 }, (_, index) => ({
        observationId: 'source-' + index,
        sequence: index + 1,
        kind: 'toolresult',
        summary: 'Captured',
        observedAt: '2026-09-09T12:00:00Z',
        recordedAt: '2026-09-09T12:00:00Z',
        agentExecutionId: null,
        runtimeReference: null,
        hasRawEvidence: true,
      })),
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.observation').length).toBe(100);
    fixture.componentInstance.reveal('source-140');
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelectorAll('.observation').length).toBe(141);
    expect(
      fixture.nativeElement
        .querySelector('#observation-source-140')
        .classList.contains('evidence-highlight'),
    ).toBe(true);
  });
});
