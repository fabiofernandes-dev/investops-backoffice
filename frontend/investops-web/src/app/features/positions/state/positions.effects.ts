import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';
import { PositionsApi } from '../../../core/api/positions.api';
import { PositionsActions } from './positions.actions';

export const loadPositions$ = createEffect(
  (actions$ = inject(Actions), api = inject(PositionsApi)) =>
    actions$.pipe(
      ofType(PositionsActions.load),
      switchMap(({ query }) =>
        api.list(query).pipe(
          map((result) => PositionsActions.loadSuccess({ result })),
          catchError(() => of(PositionsActions.loadFailure({ error: 'Falha ao carregar posições.' }))),
        ),
      ),
    ),
  { functional: true },
);
