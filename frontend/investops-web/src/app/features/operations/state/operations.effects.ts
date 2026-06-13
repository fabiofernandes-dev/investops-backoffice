import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, forkJoin, map, of, switchMap, tap } from 'rxjs';
import { OperationsApi } from '../../../core/api/operations.api';
import { OperationsActions } from './operations.actions';

function importErrorMessage(err: HttpErrorResponse): string {
  if (err.status === 422) {
    return err.error?.detail ?? 'Referência inválida (conta ou produto).';
  }
  return 'Falha ao importar a operação.';
}

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

export const importOperation$ = createEffect(
  (actions$ = inject(Actions), api = inject(OperationsApi)) =>
    actions$.pipe(
      ofType(OperationsActions.importOperation),
      switchMap(({ request }) =>
        api.import(request).pipe(
          map((result) => OperationsActions.importOperationSuccess({ id: result.id })),
          catchError((err: HttpErrorResponse) => of(OperationsActions.importOperationFailure({ error: importErrorMessage(err) }))),
        ),
      ),
    ),
  { functional: true },
);

export const importOperationNavigate$ = createEffect(
  (actions$ = inject(Actions), router = inject(Router), snackBar = inject(MatSnackBar)) =>
    actions$.pipe(
      ofType(OperationsActions.importOperationSuccess),
      tap(() => {
        snackBar.open('Operação importada com sucesso.', 'OK', { duration: 4000 });
        router.navigate(['/operations']);
      }),
    ),
  { functional: true, dispatch: false },
);
