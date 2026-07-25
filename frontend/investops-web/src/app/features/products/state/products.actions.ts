import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { Product, ProductRequest } from '../../../core/models';

export const ProductsActions = createActionGroup({
  source: 'Products',
  events: {
    Load: emptyProps(),
    'Load Success': props<{ items: Product[] }>(),
    'Load Failure': props<{ error: string }>(),
    Save: props<{ id?: string; request: ProductRequest }>(),
    'Save Success': emptyProps(),
    'Save Failure': props<{ error: string }>(),
    Remove: props<{ id: string }>(),
    'Remove Success': emptyProps(),
    'Remove Failure': props<{ error: string }>(),
  },
});
