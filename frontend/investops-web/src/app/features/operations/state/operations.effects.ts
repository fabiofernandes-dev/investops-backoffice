import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { EMPTY, catchError, delay, forkJoin, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { OperationsApi } from '../../../core/api/operations.api';
import { OperationsActions } from './operations.actions';
import { selectQuery } from './operations.feature';

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

// Reprocessa (republica o evento) e, após dar tempo ao worker, recarrega a lista com a query atual.
export const reprocessOperation$ = createEffect(
  (actions$ = inject(Actions), api = inject(OperationsApi), snackBar = inject(MatSnackBar), store = inject(Store)) =>
    actions$.pipe(
      ofType(OperationsActions.reprocessOperation),
      switchMap(({ id }) =>
        api.reprocess(id).pipe(
          tap(() => snackBar.open('Reprocessamento solicitado.', 'OK', { duration: 3000 })),
          delay(1500),
          withLatestFrom(store.select(selectQuery)),
          map(([, query]) => OperationsActions.load({ query })),
          catchError(() => {
            snackBar.open('Falha ao solicitar reprocessamento.', 'OK', { duration: 3000 });
            return EMPTY;
          }),
        ),
      ),
    ),
  { functional: true },
);
