import { createActionGroup, props } from '@ngrx/store';
import { Paged, Position, PositionsQuery } from '../../../core/models';

export const PositionsActions = createActionGroup({
  source: 'Positions',
  events: {
    Load: props<{ query: PositionsQuery }>(),
    'Load Success': props<{ result: Paged<Position> }>(),
    'Load Failure': props<{ error: string }>(),
  },
});
