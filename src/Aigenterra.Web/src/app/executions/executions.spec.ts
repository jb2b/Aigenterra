import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { ExecutionList } from './execution-list';
import { ExecutionDetail } from './execution-detail';
import { AgentList } from './agent-list';
import { ObservationTimeline } from './observation-timeline';
import { AgentExecution, ExecutionTimeline, Observation, RecentExecution } from './execution-api';

const id = '00000000-0000-0000-0000-000000000001';
const otherId = '00000000-0000-0000-0000-000000000002';
const time = '2026-09-07T12:00:00.0000003Z';
const recent: RecentExecution = {
  taskExecutionId: id,
  conversationId: otherId,
  objective: 'Inspect recorded work',
  status: 'running',
  startedAt: time,
  runtimeReference: null,
  agentCount: 2,
  observationCount: 3,
};
const agent: AgentExecution = {
  agentExecutionId: id,
  parentAgentExecutionId: null,
  status: 'running',
  startedAt: time,
  endedAt: null,
  runtimeReference: { runtime: 'codex', resourceKind: 'turn-root-agent', externalId: 'turn/1' },
};
const observation: Observation = {
  observationId: id,
  agentExecutionId: id,
  observedAt: time,
  recordedAt: time,
  sequence: 2,
  kind: 'statustransition',
  summary: 'Codex stop signal',
  runtimeReference: null,
  hasRawEvidence: true,
};
const timeline: ExecutionTimeline = {
  ...recent,
  endedAt: null,
  agents: [agent],
  observations: [observation],
};

describe('Execution screens', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('loads the bounded list, then shows the empty state', async () => {
    const fixture = TestBed.createComponent(ExecutionList);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Loading executions');
    const request = http.expectOne('/api/task-executions?limit=50');
    expect(request.request.method).toBe('GET');
    request.flush({ items: [] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('No executions recorded yet');
  });

  it('renders counts and navigates from the objective link to the detail', async () => {
    const harness = await RouterTestingHarness.create('/executions');
    http.expectOne('/api/task-executions?limit=50').flush({ items: [recent] });
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain('3 observations');
    harness.routeNativeElement!.querySelector<HTMLAnchorElement>('.objective-link')!.click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe(`/executions/${id}`);
    http.expectOne(`/api/task-executions/${id}/timeline`).flush(timeline);
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain('Observation timeline');
    const agentLink =
      harness.routeNativeElement!.querySelector<HTMLAnchorElement>('.observation-meta a')!;
    expect(agentLink.getAttribute('href')).toBe(`/executions/${id}#agent-${id}`);
    agentLink.click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe(`/executions/${id}#agent-${id}`);
    http.expectNone((request) => request.url.includes('/timeline'));
  });

  it('keeps previous data when refresh fails, and retries successfully', async () => {
    const fixture = TestBed.createComponent(ExecutionList);
    http.expectOne('/api/task-executions?limit=50').flush({ items: [recent] });
    fixture.componentInstance.load();
    http
      .expectOne('/api/task-executions?limit=50')
      .flush(null, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('API unavailable');
    expect(fixture.nativeElement.textContent).toContain('Previously loaded data');
    expect(fixture.nativeElement.textContent).toContain(recent.objective);
    fixture.nativeElement.querySelector('.notice button').click();
    http.expectOne('/api/task-executions?limit=50').flush({ items: [] });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role=alert]')).toBeNull();
  });

  it.each([404, 500])('presents a useful detail error for HTTP %s', async (status) => {
    const harness = await RouterTestingHarness.create(`/executions/${id}`);
    http
      .expectOne(`/api/task-executions/${id}/timeline`)
      .flush({ title: 'Problem', code: 'failure' }, { status, statusText: 'Error' });
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain(
      status === 404 ? 'Execution not found' : 'server could not load',
    );
    expect(harness.routeNativeElement!.querySelector('a')!.textContent).toContain('All executions');
  });

  it('does not request an invalid execution ID', async () => {
    const harness = await RouterTestingHarness.create('/executions/not-an-id');
    expect(harness.routeNativeElement!.textContent).toContain('Execution not found');
    http.expectNone((request) => request.url.includes('/timeline'));
  });

  it('cancels the previous detail request when navigating to another execution', async () => {
    const harness = await RouterTestingHarness.create(`/executions/${id}`);
    const first = http.expectOne(`/api/task-executions/${id}/timeline`);
    await harness.navigateByUrl(`/executions/${otherId}`, ExecutionDetail);
    expect(first.cancelled).toBe(true);
    http
      .expectOne(`/api/task-executions/${otherId}/timeline`)
      .flush({ ...timeline, taskExecutionId: otherId, objective: 'Second execution' });
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain('Second execution');
  });

  it('does not interpret a Stop observation as completion', async () => {
    const harness = await RouterTestingHarness.create(`/executions/${id}`);
    http.expectOne(`/api/task-executions/${id}/timeline`).flush(timeline);
    await harness.fixture.whenStable();
    expect(
      harness.routeNativeElement!.querySelector('.execution-summary .badge')!.textContent,
    ).toBe('running');
    expect(harness.routeNativeElement!.textContent).toContain(
      'stop signal does not confirm completion',
    );
  });
});

describe('Recorded facts presentation', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter(routes)] }));
  it('distinguishes root, unknown subagent parent, unknown lineage and explicit parent', async () => {
    const fixture = TestBed.createComponent(AgentList);
    fixture.componentRef.setInput('agents', [
      agent,
      {
        ...agent,
        agentExecutionId: 'sub',
        runtimeReference: { runtime: 'codex', resourceKind: 'subagent', externalId: 'sub' },
      },
      { ...agent, agentExecutionId: 'unknown', runtimeReference: null },
      { ...agent, agentExecutionId: 'child', parentAgentExecutionId: id, runtimeReference: null },
    ]);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Root agent');
    expect(fixture.nativeElement.textContent).toContain('Parent unknown');
    expect(fixture.nativeElement.textContent).toContain('Lineage unknown');
    expect(fixture.nativeElement.querySelectorAll('a').length).toBe(1);
    expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe(`/#agent-${id}`);
  });

  it('preserves server order and exact timestamps, exposes only evidence presence', async () => {
    const fixture = TestBed.createComponent(ObservationTimeline);
    fixture.componentRef.setInput('agents', [agent]);
    fixture.componentRef.setInput('observations', [
      observation,
      {
        ...observation,
        observationId: otherId,
        sequence: 1,
        observedAt: '2026-09-07T12:00:00.0000004Z',
        hasRawEvidence: false,
        agentExecutionId: null,
      },
    ]);
    await fixture.whenStable();
    const rows = fixture.nativeElement.querySelectorAll('.observation');
    expect(rows[0].textContent).toContain('Sequence 2');
    expect(rows[1].textContent).toContain('Sequence 1');
    expect(rows[0].textContent).toContain(time);
    expect(fixture.nativeElement.querySelectorAll('.evidence').length).toBe(1);
    expect(rows[1].textContent).toContain('No agent associated');
  });

  it('renders 100 at a time and expands long plain text safely', async () => {
    const fixture = TestBed.createComponent(ObservationTimeline);
    fixture.componentRef.setInput('agents', []);
    fixture.componentRef.setInput(
      'observations',
      Array.from({ length: 1000 }, (_, i) => ({
        ...observation,
        observationId: String(i),
        summary: '<script>alert(1)</script>' + 'long summary '.repeat(100),
      })),
    );
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelectorAll('.observation').length).toBe(100);
    expect(fixture.nativeElement.querySelector('script')).toBeNull();
    fixture.nativeElement.querySelector('.text-button').click();
    await fixture.whenStable();
    expect(
      fixture.nativeElement.querySelector('.observation-summary').classList.contains('clamped'),
    ).toBe(false);
    fixture.nativeElement.querySelector('.load-more button').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelectorAll('.observation').length).toBe(200);
  });
});
