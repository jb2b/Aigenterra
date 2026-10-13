import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { RepositoryContextPanel } from './repository-context';
import { RepositoryContext } from './execution-api';

const context: RepositoryContext = {
  capturedAt: '2026-09-16T18:31:38.4703161+02:00',
  status: 'captured',
  repositoryRoot: 'C:/source/main',
  commonGitDirectory: 'C:/source/main/.git',
  worktreePath: 'C:/source/feature',
  branch: 'feature',
  headSha: 'a'.repeat(40),
  isDirty: false,
  changedFiles: [],
  untrackedFiles: [],
};
function render(start?: RepositoryContext | null, end?: RepositoryContext | null) {
  const fixture = TestBed.createComponent(RepositoryContextPanel);
  fixture.componentRef.setInput('start', start);
  fixture.componentRef.setInput('end', end);
  fixture.detectChanges();
  return fixture;
}
describe('Repository context', () => {
  const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
  beforeEach(() =>
    Object.defineProperty(navigator, 'clipboard', { configurable: true, get: () => undefined }),
  );
  afterEach(() => {
    vi.restoreAllMocks();
    if (clipboardDescriptor) Object.defineProperty(navigator, 'clipboard', clipboardDescriptor);
    else Reflect.deleteProperty(navigator, 'clipboard');
  });

  it('renders both recorded snapshots, full SHAs, exact times and explicit clean/dirty states', () => {
    const fixture = render(context, { ...context, headSha: 'b'.repeat(40), isDirty: true });
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelectorAll('.repository-snapshot').length).toBe(2);
    for (const value of [
      context.repositoryRoot,
      context.worktreePath,
      context.commonGitDirectory,
      context.branch,
      context.headSha,
      context.capturedAt,
      'b'.repeat(40),
      'Clean',
      'Dirty',
      'HEAD changed',
    ]) {
      expect(element.textContent).toContain(value);
    }
    expect(element.querySelector<HTMLDetailsElement>('.repository-exact')!.open).toBe(false);
  });

  it.each([undefined, null])('handles recordings without context (%s)', (missing) => {
    const element: HTMLElement = render(missing, missing).nativeElement;
    expect(element.textContent).toContain('No repository context recorded');
    expect(element.querySelector('.repository-snapshots')).toBeNull();
  });

  it('does not infer detached HEAD, clean state or HEAD changes from missing fields', () => {
    const element: HTMLElement = render({
      status: 'captured',
      headSha: context.headSha,
    }).nativeElement;
    expect(element.textContent).toContain('Unknown / not recorded');
    expect(element.textContent).toContain('No end snapshot recorded');
    expect(element.textContent).not.toContain('Detached');
    expect(element.textContent).not.toContain('Clean');
    expect(element.textContent).not.toContain('HEAD changed');
    expect(element.textContent).toContain('changed: unknown');
  });

  it.each([
    ['timeout', 'Timed out'],
    ['incomplete', 'Incomplete'],
    ['git_unavailable', 'Git unavailable'],
    ['not_repository', 'Not a Git repository'],
    ['output_limit', 'Output limit reached'],
    ['git_error', 'Git error'],
    ['changed_during_capture', 'Changed during capture'],
    ['future_status', 'Unrecognized capture status'],
  ])('preserves status %s and available partial fields', (status, label) => {
    const element: HTMLElement = render({
      status,
      repositoryRoot: context.repositoryRoot,
    }).nativeElement;
    expect(element.textContent).toContain(label);
    expect(element.textContent).toContain(`(${status})`);
    expect(element.textContent).toContain(context.repositoryRoot);
    expect(element.textContent).toContain('Not recorded');
  });

  it('expands exact file paths and distinguishes empty from unrecorded lists', () => {
    const longPath = 'nested/'.repeat(30) + '<file>.txt';
    const fixture = render(
      { ...context, changedFiles: [longPath], untrackedFiles: ['new.txt'] },
      { ...context, changedFiles: [], untrackedFiles: null },
    );
    const element: HTMLElement = fixture.nativeElement;
    const lists = element.querySelectorAll<HTMLDetailsElement>('.repository-files');
    expect(lists[0].open).toBe(false);
    lists[0].querySelector('summary')!.click();
    expect(lists[0].open).toBe(true);
    expect(lists[0].textContent).toContain('changed: 1, untracked: 1');
    expect(lists[0].querySelector('li code')!.textContent).toBe(longPath);
    expect(lists[0].textContent).toContain('new.txt');
    expect(lists[0].textContent).toContain(
      'Staged and unstaged changes were not recorded separately',
    );
    expect(lists[1].textContent).toContain('None recorded');
    expect(lists[1].textContent).toContain('Not recorded');
    expect(element.textContent).not.toContain('HEAD changed');
  });

  it('copies the full SHA through the button and announces success', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.spyOn(navigator, 'clipboard', 'get').mockReturnValue({ writeText } as unknown as Clipboard);
    const fixture = render(context);
    fixture.nativeElement.querySelector('button').click();
    await fixture.whenStable();
    expect(writeText).toHaveBeenCalledWith(context.headSha);
    expect(fixture.nativeElement.querySelector('[role=status]').textContent).toContain(
      'SHA copied',
    );
  });

  it.each([false, true])(
    'keeps the SHA selectable when clipboard fails (missing: %s)',
    async (missing) => {
      vi.spyOn(navigator, 'clipboard', 'get').mockReturnValue(
        missing
          ? undefined!
          : ({
              writeText: vi.fn().mockRejectedValue(new Error('Denied')),
            } as unknown as Clipboard),
      );
      const fixture = render(context);
      await fixture.componentInstance.copySha(context.headSha!, 'Start');
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[role=status]').textContent).toContain(
        'Select the SHA',
      );
      expect(fixture.nativeElement.querySelector('.repository-sha').textContent).toBe(
        context.headSha,
      );
    },
  );
});
