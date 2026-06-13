import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import * as operationsEffects from './features/operations/state/operations.effects';
import { operationsFeature } from './features/operations/state/operations.feature';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
  },
  {
    path: 'operations',
    providers: [provideState(operationsFeature), provideEffects(operationsEffects)],
    children: [
      {
        path: '',
        loadComponent: () => import('./features/operations/list/operations-list.component').then((m) => m.OperationsListComponent),
      },
      {
        path: ':id',
        loadComponent: () => import('./features/operations/detail/operation-detail.component').then((m) => m.OperationDetailComponent),
      },
    ],
  },
  {
    path: 'positions',
    loadComponent: () => import('./features/positions/positions.component').then((m) => m.PositionsComponent),
  },
  { path: '**', redirectTo: '' },
];
