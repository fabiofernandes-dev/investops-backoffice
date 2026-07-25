import { createFeature, createReducer, on } from '@ngrx/store';
import { Product } from '../../../core/models';
import { ProductsActions } from './products.actions';

export interface ProductsState {
  items: Product[];
  loading: boolean;
  error: string | null;
  saving: boolean;
  saveError: string | null;
}

export const initialProductsState: ProductsState = {
  items: [],
  loading: false,
  error: null,
  saving: false,
  saveError: null,
};

export const productsFeature = createFeature({
  name: 'products',
  reducer: createReducer(
    initialProductsState,
    on(ProductsActions.load, (state) => ({ ...state, loading: true, error: null })),
    on(ProductsActions.loadSuccess, (state, { items }) => ({ ...state, items, loading: false })),
    on(ProductsActions.loadFailure, (state, { error }) => ({ ...state, loading: false, error })),
    on(ProductsActions.save, (state) => ({ ...state, saving: true, saveError: null })),
    on(ProductsActions.saveSuccess, (state) => ({ ...state, saving: false })),
    on(ProductsActions.saveFailure, (state, { error }) => ({ ...state, saving: false, saveError: error })),
  ),
});

export const { selectItems, selectLoading, selectError, selectSaving, selectSaveError } = productsFeature;
