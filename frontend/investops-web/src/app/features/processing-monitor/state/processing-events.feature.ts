import { createFeature, createReducer, on } from '@ngrx/store';
import { ProcessingEventItem, ProcessingEventsQuery } from '../../../core/models';
import { ProcessingEventsActions } from './processing-events.actions';

export interface ProcessingEventsState {
  query: ProcessingEventsQuery;
  items: ProcessingEventItem[];
  total: number;
  loading: boolean;
  error: string | null;
}

export const initialProcessingEventsState: ProcessingEventsState = {
  query: { page: 1, pageSize: 20, sort: 'createdAt:desc' },
  items: [],
  total: 0,
  loading: false,
  error: null,
};

export const processingEventsFeature = createFeature({
  name: 'processingEvents',
  reducer: createReducer(
    initialProcessingEventsState,
    on(ProcessingEventsActions.load, (state, { query, silent }) => ({ ...state, query, loading: !silent, error: null })),
    on(ProcessingEventsActions.loadSuccess, (state, { result }) => ({
      ...state,
      items: result.items,
      total: result.total,
      loading: false,
    })),
    on(ProcessingEventsActions.loadFailure, (state, { error }) => ({ ...state, loading: false, error })),
  ),
});

export const { selectItems, selectTotal, selectLoading, selectError, selectQuery } = processingEventsFeature;
