import { createFeature, createReducer, on } from '@ngrx/store';
import { Position, PositionsQuery } from '../../../core/models';
import { PositionsActions } from './positions.actions';

export interface PositionsState {
  query: PositionsQuery;
  items: Position[];
  total: number;
  loading: boolean;
  error: string | null;
}

export const initialPositionsState: PositionsState = {
  query: { page: 1, pageSize: 20, sort: 'updatedAt:desc' },
  items: [],
  total: 0,
  loading: false,
  error: null,
};

export const positionsFeature = createFeature({
  name: 'positions',
  reducer: createReducer(
    initialPositionsState,
    on(PositionsActions.load, (state, { query }) => ({ ...state, query, loading: true, error: null })),
    on(PositionsActions.loadSuccess, (state, { result }) => ({
      ...state,
      items: result.items,
      total: result.total,
      loading: false,
    })),
    on(PositionsActions.loadFailure, (state, { error }) => ({ ...state, loading: false, error })),
  ),
});

export const { selectItems, selectTotal, selectLoading, selectError, selectQuery } = positionsFeature;
