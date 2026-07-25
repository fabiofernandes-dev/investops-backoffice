import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, concatMap, exhaustMap, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
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

// exhaustMap: ignora cliques repetidos de "Importar" enquanto o POST está em voo.
export const importStatement$ = createEffect(
  (actions$ = inject(Actions), api = inject(ReconciliationApi)) =>
    actions$.pipe(
      ofType(ReconciliationActions.import),
      exhaustMap(({ source, referenceDate, file }) =>
        api.import(source, referenceDate, file).pipe(
          map((result) => ReconciliationActions.importSuccess({ result })),
          catchError((err: HttpErrorResponse) => of(ReconciliationActions.importFailure({ error: importErrorMessage(err) }))),
        ),
      ),
    ),
  { functional: true },
);

// O reset de formulário/filtro e o reload ficam no componente (estado local);
// aqui apenas o resumo em snackbar.
export const importStatementNotify$ = createEffect(
  (actions$ = inject(Actions), snackBar = inject(MatSnackBar)) =>
    actions$.pipe(
      ofType(ReconciliationActions.importSuccess),
      tap(({ result }) =>
        snackBar.open(`${result.divergenceCount} divergência(s), ${result.matchedCount} conferida(s).`, 'OK', { duration: 4000 }),
      ),
    ),
  { functional: true, dispatch: false },
);

// concatMap: escritas por-entidade não podem se cancelar entre si (evita perder um POST).
// O catchError segue o fluxo para recarregar a lista e ressincronizar linhas obsoletas (ex.: 409 já resolvida).
export const resolveDivergence$ = createEffect(
  (actions$ = inject(Actions), api = inject(ReconciliationApi), snackBar = inject(MatSnackBar), store = inject(Store)) =>
    actions$.pipe(
      ofType(ReconciliationActions.resolve),
      concatMap(({ id }) =>
        api.resolve(id).pipe(
          tap(() => snackBar.open('Divergência resolvida.', 'OK', { duration: 3000 })),
          catchError((err: HttpErrorResponse) => {
            snackBar.open(err.error?.detail ?? 'Falha ao resolver a divergência.', 'OK', { duration: 3000 });
            return of(void 0);
          }),
          withLatestFrom(store.select(selectQuery)),
          map(([, query]) => ReconciliationActions.load({ query })),
        ),
      ),
    ),
  { functional: true },
);

export const ignoreDivergence$ = createEffect(
  (actions$ = inject(Actions), api = inject(ReconciliationApi), snackBar = inject(MatSnackBar), store = inject(Store)) =>
    actions$.pipe(
      ofType(ReconciliationActions.ignore),
      concatMap(({ id }) =>
        api.ignore(id).pipe(
          tap(() => snackBar.open('Divergência ignorada.', 'OK', { duration: 3000 })),
          catchError((err: HttpErrorResponse) => {
            snackBar.open(err.error?.detail ?? 'Falha ao ignorar a divergência.', 'OK', { duration: 3000 });
            return of(void 0);
          }),
          withLatestFrom(store.select(selectQuery)),
          map(([, query]) => ReconciliationActions.load({ query })),
        ),
      ),
    ),
  { functional: true },
);
