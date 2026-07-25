import { createFeature, createReducer, on } from '@ngrx/store';
import { Divergence, DivergencesQuery, ImportReconciliationResult } from '../../../core/models';
import { ReconciliationActions } from './reconciliation.actions';

export interface ReconciliationState {
  query: DivergencesQuery;
  items: Divergence[];
  total: number;
  loading: boolean;
  error: string | null;
  importing: boolean;
  importError: string | null;
  lastImport: ImportReconciliationResult | null;
}

export const initialReconciliationState: ReconciliationState = {
  query: { page: 1, pageSize: 20, sort: 'createdAt:desc' },
  items: [],
  total: 0,
  loading: false,
  error: null,
  importing: false,
  importError: null,
  lastImport: null,
};

export const reconciliationFeature = createFeature({
  name: 'reconciliation',
  reducer: createReducer(
    initialReconciliationState,
    on(ReconciliationActions.load, (state, { query }) => ({ ...state, query, loading: true, error: null })),
    on(ReconciliationActions.loadSuccess, (state, { result }) => ({
      ...state,
      items: result.items,
      total: result.total,
      loading: false,
    })),
    on(ReconciliationActions.loadFailure, (state, { error }) => ({ ...state, loading: false, error })),
    on(ReconciliationActions.import, (state) => ({ ...state, importing: true, importError: null })),
    on(ReconciliationActions.importSuccess, (state, { result }) => ({ ...state, importing: false, lastImport: result })),
    on(ReconciliationActions.importFailure, (state, { error }) => ({ ...state, importing: false, importError: error })),
  ),
});

export const {
  selectItems,
  selectTotal,
  selectLoading,
  selectError,
  selectQuery,
  selectImporting,
  selectImportError,
  selectLastImport,
} = reconciliationFeature;
