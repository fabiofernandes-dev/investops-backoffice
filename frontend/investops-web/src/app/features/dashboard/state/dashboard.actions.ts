import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { DashboardSummary, ProcessingMetrics } from '../../../core/models';

export const DashboardActions = createActionGroup({
  source: 'Dashboard',
  events: {
    Load: emptyProps(),
    'Load Success': props<{ summary: DashboardSummary; metrics: ProcessingMetrics }>(),
    'Load Failure': props<{ error: string }>(),
  },
});
