import { createFeature, createReducer, on } from '@ngrx/store';
import { AuditLog, OperationDetail, OperationListItem, OperationsQuery, ProcessingEvent } from '../../../core/models';
import { OperationsActions } from './operations.actions';

export interface OperationsState {
  query: OperationsQuery;
  items: OperationListItem[];
  total: number;
  loading: boolean;
  error: string | null;
  detail: OperationDetail | null;
  events: ProcessingEvent[];
  audit: AuditLog[];
  detailLoading: boolean;
  detailError: string | null;
}

export const initialOperationsState: OperationsState = {
  query: { page: 1, pageSize: 20, sort: 'operationDate:desc' },
  items: [],
  total: 0,
  loading: false,
  error: null,
  detail: null,
  events: [],
  audit: [],
  detailLoading: false,
  detailError: null,
};

export const operationsFeature = createFeature({
  name: 'operations',
  reducer: createReducer(
    initialOperationsState,
    on(OperationsActions.load, (state, { query }) => ({ ...state, query, loading: true, error: null })),
    on(OperationsActions.loadSuccess, (state, { result }) => ({
      ...state,
      items: result.items,
      total: result.total,
      loading: false,
    })),
    on(OperationsActions.loadFailure, (state, { error }) => ({ ...state, loading: false, error })),
    on(OperationsActions.loadDetail, (state) => ({
      ...state,
      detailLoading: true,
      detailError: null,
      detail: null,
      events: [],
      audit: [],
    })),
    on(OperationsActions.loadDetailSuccess, (state, { detail, events, audit }) => ({
      ...state,
      detail,
      events,
      audit,
      detailLoading: false,
    })),
    on(OperationsActions.loadDetailFailure, (state, { error }) => ({ ...state, detailLoading: false, detailError: error })),
  ),
});

export const {
  selectItems,
  selectTotal,
  selectLoading,
  selectError,
  selectQuery,
  selectDetail,
  selectEvents,
  selectAudit,
  selectDetailLoading,
  selectDetailError,
} = operationsFeature;
