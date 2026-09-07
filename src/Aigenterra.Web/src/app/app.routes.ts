import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'executions' },
  {
    path: 'executions',
    loadComponent: () => import('./executions/execution-list').then((m) => m.ExecutionList),
    title: 'Executions · Aigenterra',
  },
  {
    path: 'executions/:id',
    loadComponent: () => import('./executions/execution-detail').then((m) => m.ExecutionDetail),
    title: 'Execution · Aigenterra',
  },
  {
    path: '**',
    loadComponent: () => import('./executions/not-found').then((m) => m.NotFound),
    title: 'Not found · Aigenterra',
  },
];
