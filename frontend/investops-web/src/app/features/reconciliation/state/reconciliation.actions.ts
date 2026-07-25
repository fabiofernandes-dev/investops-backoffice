import { createActionGroup, props } from '@ngrx/store';
import { Divergence, DivergencesQuery, ImportReconciliationResult, Paged } from '../../../core/models';

export const ReconciliationActions = createActionGroup({
  source: 'Reconciliation',
  events: {
    Load: props<{ query: DivergencesQuery }>(),
    'Load Success': props<{ result: Paged<Divergence> }>(),
    'Load Failure': props<{ error: string }>(),
    Import: props<{ source: string; referenceDate: string; file: File }>(),
    'Import Success': props<{ result: ImportReconciliationResult }>(),
    'Import Failure': props<{ error: string }>(),
    Resolve: props<{ id: string }>(),
    Ignore: props<{ id: string }>(),
  },
});
