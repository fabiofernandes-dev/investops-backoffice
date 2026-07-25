import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Observable, catchError, concatMap, exhaustMap, map, of, switchMap, tap } from 'rxjs';
import { ProductsApi } from '../../../core/api/products.api';
import { ProductsActions } from './products.actions';

function saveErrorMessage(err: HttpErrorResponse): string {
  if (err.status === 409) {
    return 'Ticker já existe.';
  }
  if (err.status === 400 || err.status === 422) {
    return 'Dados inválidos.';
  }
  return 'Falha ao salvar o produto.';
}

export const loadProducts$ = createEffect(
  (actions$ = inject(Actions), api = inject(ProductsApi)) =>
    actions$.pipe(
      ofType(ProductsActions.load),
      switchMap(() =>
        api.list().pipe(
          map((items) => ProductsActions.loadSuccess({ items })),
          catchError(() => of(ProductsActions.loadFailure({ error: 'Falha ao carregar produtos.' }))),
        ),
      ),
    ),
  { functional: true },
);

// exhaustMap: ignora cliques repetidos de "Salvar" enquanto a escrita está em voo.
export const saveProduct$ = createEffect(
  (actions$ = inject(Actions), api = inject(ProductsApi)) =>
    actions$.pipe(
      ofType(ProductsActions.save),
      exhaustMap(({ id, request }) => {
        const write$: Observable<unknown> = id ? api.update(id, request) : api.create(request);
        return write$.pipe(
          map(() => ProductsActions.saveSuccess()),
          catchError((err: HttpErrorResponse) => of(ProductsActions.saveFailure({ error: saveErrorMessage(err) }))),
        );
      }),
    ),
  { functional: true },
);

// dispatch:false — a navegacao para /products remonta a lista, cujo ngOnInit ja dispara load().
export const saveProductSuccess$ = createEffect(
  (actions$ = inject(Actions), router = inject(Router), snackBar = inject(MatSnackBar)) =>
    actions$.pipe(
      ofType(ProductsActions.saveSuccess),
      tap(() => {
        snackBar.open('Produto salvo com sucesso.', 'OK', { duration: 4000 });
        router.navigate(['/products']);
      }),
    ),
  { functional: true, dispatch: false },
);

// concatMap: escritas por-entidade não podem se cancelar entre si (evita perder um DELETE).
export const removeProduct$ = createEffect(
  (actions$ = inject(Actions), api = inject(ProductsApi)) =>
    actions$.pipe(
      ofType(ProductsActions.remove),
      concatMap(({ id }) =>
        api.remove(id).pipe(
          map(() => ProductsActions.removeSuccess()),
          catchError(() => of(ProductsActions.removeFailure({ error: 'Falha ao excluir o produto.' }))),
        ),
      ),
    ),
  { functional: true },
);

export const removeProductSuccess$ = createEffect(
  (actions$ = inject(Actions), snackBar = inject(MatSnackBar)) =>
    actions$.pipe(
      ofType(ProductsActions.removeSuccess),
      tap(() => snackBar.open('Produto excluído.', 'OK', { duration: 3000 })),
      map(() => ProductsActions.load()),
    ),
  { functional: true },
);

export const removeProductFailure$ = createEffect(
  (actions$ = inject(Actions), snackBar = inject(MatSnackBar)) =>
    actions$.pipe(
      ofType(ProductsActions.removeFailure),
      tap(({ error }) => snackBar.open(error, 'OK', { duration: 3000 })),
    ),
  { functional: true, dispatch: false },
);
