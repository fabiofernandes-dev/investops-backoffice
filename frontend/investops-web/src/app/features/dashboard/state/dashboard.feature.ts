import { createFeature, createReducer, on } from '@ngrx/store';
import { DashboardSummary, ProcessingMetrics } from '../../../core/models';
import { DashboardActions } from './dashboard.actions';

export interface DashboardState {
  summary: DashboardSummary | null;
  metrics: ProcessingMetrics | null;
  loading: boolean;
  error: string | null;
}

export const initialDashboardState: DashboardState = {
  summary: null,
  metrics: null,
  loading: false,
  error: null,
};

export const dashboardFeature = createFeature({
  name: 'dashboard',
  reducer: createReducer(
    initialDashboardState,
    on(DashboardActions.load, (state) => ({ ...state, loading: true, error: null })),
    on(DashboardActions.loadSuccess, (state, { summary, metrics }) => ({ ...state, summary, metrics, loading: false })),
    on(DashboardActions.loadFailure, (state, { error }) => ({ ...state, loading: false, error })),
  ),
});

export const { selectSummary, selectMetrics, selectLoading, selectError } = dashboardFeature;
