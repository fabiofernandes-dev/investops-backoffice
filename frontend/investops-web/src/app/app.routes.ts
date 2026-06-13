import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import * as operationsEffects from './features/operations/state/operations.effects';
import { operationsFeature } from './features/operations/state/operations.feature';
import * as positionsEffects from './features/positions/state/positions.effects';
import { positionsFeature } from './features/positions/state/positions.feature';
import * as dashboardEffects from './features/dashboard/state/dashboard.effects';
import { dashboardFeature } from './features/dashboard/state/dashboard.feature';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    providers: [provideState(dashboardFeature), provideEffects(dashboardEffects)],
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
        path: 'new',
        loadComponent: () => import('./features/operations/import/operation-import.component').then((m) => m.OperationImportComponent),
      },
      {
        path: ':id',
        loadComponent: () => import('./features/operations/detail/operation-detail.component').then((m) => m.OperationDetailComponent),
      },
    ],
  },
  {
    path: 'positions',
    providers: [provideState(positionsFeature), provideEffects(positionsEffects)],
    loadComponent: () => import('./features/positions/positions.component').then((m) => m.PositionsComponent),
  },
  { path: '**', redirectTo: '' },
];
