import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
  },
  {
    path: 'operations',
    loadComponent: () => import('./features/operations/list/operations-list.component').then((m) => m.OperationsListComponent),
  },
  {
    path: 'operations/:id',
    loadComponent: () => import('./features/operations/detail/operation-detail.component').then((m) => m.OperationDetailComponent),
  },
  {
    path: 'positions',
    loadComponent: () => import('./features/positions/positions.component').then((m) => m.PositionsComponent),
  },
  { path: '**', redirectTo: '' },
];
