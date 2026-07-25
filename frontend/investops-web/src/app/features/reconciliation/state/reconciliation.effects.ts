import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { EMPTY, catchError, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { ReconciliationApi } from '../../../core/api/reconciliation.api';
import { ReconciliationActions } from './reconciliation.actions';
import { selectQuery } from './reconciliation.feature';

function importErrorMessage(err: HttpErrorResponse): string {
  if (err.status === 422) {
    return err.error?.detail ?? 'Referência inválida (conta ou produto).';
  }
  if (err.status === 400) {
    return err.error?.detail ?? 'CSV inválido.';
  }
  return 'Falha ao importar o extrato.';
}

export const loadDivergences$ = createEffect(
  (actions$ = inject(Actions), api = inject(ReconciliationApi)) =>
    actions$.pipe(
      ofType(ReconciliationActions.load),
      switchMap(({ query }) =>
        api.listDivergences(query).pipe(
          map((result) => ReconciliationActions.loadSuccess({ result })),
          catchError(() => of(ReconciliationActions.loadFailure({ error: 'Falha ao carregar divergências.' }))),
        ),
      ),
    ),
  { functional: true },
);

export const importStatement$ = createEffect(
  (actions$ = inject(Actions), api = inject(ReconciliationApi)) =>
    actions$.pipe(
      ofType(ReconciliationActions.import),
      switchMap(({ source, referenceDate, file }) =>
        api.import(source, referenceDate, file).pipe(
          map((result) => ReconciliationActions.importSuccess({ result })),
          catchError((err: HttpErrorResponse) => of(ReconciliationActions.importFailure({ error: importErrorMessage(err) }))),
        ),
      ),
    ),
  { functional: true },
);

// Após um import bem-sucedido, mostra o resumo e recarrega a tabela com a query atual.
export const importStatementReload$ = createEffect(
  (actions$ = inject(Actions), snackBar = inject(MatSnackBar), store = inject(Store)) =>
    actions$.pipe(
      ofType(ReconciliationActions.importSuccess),
      tap(({ result }) =>
        snackBar.open(
          `${result.divergenceCount} divergência(s), ${result.matchedCount} conferida(s).`,
          'OK',
          { duration: 4000 },
        ),
      ),
      withLatestFrom(store.select(selectQuery)),
      map(([, query]) => ReconciliationActions.load({ query })),
    ),
  { functional: true },
);

export const resolveDivergence$ = createEffect(
  (actions$ = inject(Actions), api = inject(ReconciliationApi), snackBar = inject(MatSnackBar), store = inject(Store)) =>
    actions$.pipe(
      ofType(ReconciliationActions.resolve),
      switchMap(({ id }) =>
        api.resolve(id).pipe(
          tap(() => snackBar.open('Divergência resolvida.', 'OK', { duration: 3000 })),
          withLatestFrom(store.select(selectQuery)),
          map(([, query]) => ReconciliationActions.load({ query })),
          catchError(() => {
            snackBar.open('Falha ao resolver a divergência.', 'OK', { duration: 3000 });
            return EMPTY;
          }),
        ),
      ),
    ),
  { functional: true },
);

export const ignoreDivergence$ = createEffect(
  (actions$ = inject(Actions), api = inject(ReconciliationApi), snackBar = inject(MatSnackBar), store = inject(Store)) =>
    actions$.pipe(
      ofType(ReconciliationActions.ignore),
      switchMap(({ id }) =>
        api.ignore(id).pipe(
          tap(() => snackBar.open('Divergência ignorada.', 'OK', { duration: 3000 })),
          withLatestFrom(store.select(selectQuery)),
          map(([, query]) => ReconciliationActions.load({ query })),
          catchError(() => {
            snackBar.open('Falha ao ignorar a divergência.', 'OK', { duration: 3000 });
            return EMPTY;
          }),
        ),
      ),
    ),
  { functional: true },
);
