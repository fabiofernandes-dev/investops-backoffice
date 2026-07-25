import { createActionGroup, props } from '@ngrx/store';
import { Paged, ProcessingEventItem, ProcessingEventsQuery } from '../../../core/models';

export const ProcessingEventsActions = createActionGroup({
  source: 'Processing Events',
  events: {
    Load: props<{ query: ProcessingEventsQuery; silent?: boolean }>(),
    'Load Success': props<{ result: Paged<ProcessingEventItem> }>(),
    'Load Failure': props<{ error: string }>(),
  },
});
