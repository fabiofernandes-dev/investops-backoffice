import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { DashboardApi } from '../../../core/api/dashboard.api';
import { DashboardActions } from './dashboard.actions';

export const loadDashboard$ = createEffect(
  (actions$ = inject(Actions), api = inject(DashboardApi)) =>
    actions$.pipe(
      ofType(DashboardActions.load),
      switchMap(() =>
        forkJoin({ summary: api.summary(), metrics: api.processingMetrics() }).pipe(
          map(({ summary, metrics }) => DashboardActions.loadSuccess({ summary, metrics })),
          catchError(() => of(DashboardActions.loadFailure({ error: 'Falha ao carregar o dashboard.' }))),
        ),
      ),
    ),
  { functional: true },
);
