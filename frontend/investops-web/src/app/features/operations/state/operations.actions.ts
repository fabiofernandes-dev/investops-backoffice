import { createActionGroup, props } from '@ngrx/store';
import { AuditLog, OperationDetail, OperationListItem, OperationsQuery, Paged, ProcessingEvent } from '../../../core/models';

export const OperationsActions = createActionGroup({
  source: 'Operations',
  events: {
    Load: props<{ query: OperationsQuery }>(),
    'Load Success': props<{ result: Paged<OperationListItem> }>(),
    'Load Failure': props<{ error: string }>(),
    'Load Detail': props<{ id: string }>(),
    'Load Detail Success': props<{ detail: OperationDetail; events: ProcessingEvent[]; audit: AuditLog[] }>(),
    'Load Detail Failure': props<{ error: string }>(),
  },
});
