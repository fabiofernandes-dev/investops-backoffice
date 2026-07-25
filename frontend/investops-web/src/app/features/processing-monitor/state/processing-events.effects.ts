import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';
import { ProcessingEventsApi } from '../../../core/api/processing-events.api';
import { ProcessingEventsActions } from './processing-events.actions';

export const loadProcessingEvents$ = createEffect(
  (actions$ = inject(Actions), api = inject(ProcessingEventsApi)) =>
    actions$.pipe(
      ofType(ProcessingEventsActions.load),
      switchMap(({ query }) =>
        api.list(query).pipe(
          map((result) => ProcessingEventsActions.loadSuccess({ result })),
          catchError(() => of(ProcessingEventsActions.loadFailure({ error: 'Falha ao carregar os eventos de processamento.' }))),
        ),
      ),
    ),
  { functional: true },
);
