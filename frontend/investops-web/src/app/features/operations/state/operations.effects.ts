import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { OperationsApi } from '../../../core/api/operations.api';
import { OperationsActions } from './operations.actions';

export const loadOperations$ = createEffect(
  (actions$ = inject(Actions), api = inject(OperationsApi)) =>
    actions$.pipe(
      ofType(OperationsActions.load),
      switchMap(({ query }) =>
        api.list(query).pipe(
          map((result) => OperationsActions.loadSuccess({ result })),
          catchError(() => of(OperationsActions.loadFailure({ error: 'Falha ao carregar operações.' }))),
        ),
      ),
    ),
  { functional: true },
);

export const loadOperationDetail$ = createEffect(
  (actions$ = inject(Actions), api = inject(OperationsApi)) =>
    actions$.pipe(
      ofType(OperationsActions.loadDetail),
      switchMap(({ id }) =>
        forkJoin({
          detail: api.getById(id),
          events: api.events(id),
          audit: api.audit(id),
        }).pipe(
          map(({ detail, events, audit }) => OperationsActions.loadDetailSuccess({ detail, events, audit })),
          catchError(() => of(OperationsActions.loadDetailFailure({ error: 'Falha ao carregar o detalhe da operação.' }))),
        ),
      ),
    ),
  { functional: true },
);
