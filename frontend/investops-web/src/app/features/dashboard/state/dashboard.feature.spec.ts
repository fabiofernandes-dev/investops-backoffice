import { DashboardSummary, ProcessingMetrics } from '../../../core/models';
import { DashboardActions } from './dashboard.actions';
import { dashboardFeature, initialDashboardState } from './dashboard.feature';

const reducer = dashboardFeature.reducer;

describe('dashboard reducer', () => {
  it('load sets loading and clears error', () => {
    const state = reducer({ ...initialDashboardState, error: 'x' }, DashboardActions.load());
    expect(state.loading).toBe(true);
    expect(state.error).toBeNull();
  });

  it('loadSuccess populates summary and metrics', () => {
    const summary: DashboardSummary = {
      importedToday: 2,
      processed: 5,
      rejected: 1,
      failed: 0,
      divergencesPending: 0,
      averageProcessingSeconds: 1.5,
    };
    const metrics: ProcessingMetrics = { operationsByStatus: { Processed: 5 }, outboxPending: 0 };
    const state = reducer({ ...initialDashboardState, loading: true }, DashboardActions.loadSuccess({ summary, metrics }));
    expect(state.summary?.processed).toBe(5);
    expect(state.metrics?.operationsByStatus['Processed']).toBe(5);
    expect(state.loading).toBe(false);
  });

  it('loadFailure sets error and stops loading', () => {
    const state = reducer({ ...initialDashboardState, loading: true }, DashboardActions.loadFailure({ error: 'boom' }));
    expect(state.error).toBe('boom');
    expect(state.loading).toBe(false);
  });
});
