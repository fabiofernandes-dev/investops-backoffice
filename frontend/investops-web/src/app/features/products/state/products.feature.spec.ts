import { Product } from '../../../core/models';
import { ProductsActions } from './products.actions';
import { initialProductsState, productsFeature } from './products.feature';

const reducer = productsFeature.reducer;

function makeProduct(id: string): Product {
  return {
    id,
    productType: 'Stock',
    name: 'Produto ' + id,
    ticker: 'TCK' + id,
    riskLevel: 3,
    liquidityDays: 1,
    isActive: true,
  };
}

describe('products reducer', () => {
  it('load sets loading and clears error', () => {
    const state = reducer({ ...initialProductsState, error: 'x' }, ProductsActions.load());
    expect(state.loading).toBe(true);
    expect(state.error).toBeNull();
  });

  it('loadSuccess populates items and stops loading', () => {
    const state = reducer(
      { ...initialProductsState, loading: true },
      ProductsActions.loadSuccess({ items: [makeProduct('1'), makeProduct('2')] }),
    );
    expect(state.items).toHaveLength(2);
    expect(state.loading).toBe(false);
  });

  it('loadFailure sets error and stops loading', () => {
    const state = reducer({ ...initialProductsState, loading: true }, ProductsActions.loadFailure({ error: 'boom' }));
    expect(state.error).toBe('boom');
    expect(state.loading).toBe(false);
  });

  it('save sets saving and clears saveError', () => {
    const state = reducer(
      { ...initialProductsState, saveError: 'x' },
      ProductsActions.save({ request: { productType: 'Stock', name: 'A', ticker: null, riskLevel: 1, liquidityDays: 0 } }),
    );
    expect(state.saving).toBe(true);
    expect(state.saveError).toBeNull();
  });

  it('saveSuccess stops saving', () => {
    const state = reducer({ ...initialProductsState, saving: true }, ProductsActions.saveSuccess());
    expect(state.saving).toBe(false);
  });

  it('saveFailure sets saveError and stops saving', () => {
    const state = reducer(
      { ...initialProductsState, saving: true },
      ProductsActions.saveFailure({ error: 'Ticker já existe.' }),
    );
    expect(state.saving).toBe(false);
    expect(state.saveError).toBe('Ticker já existe.');
  });
});
