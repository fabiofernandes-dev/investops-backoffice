import { Paged, Position } from '../../../core/models';
import { PositionsActions } from './positions.actions';
import { initialPositionsState, positionsFeature } from './positions.feature';

const reducer = positionsFeature.reducer;

function makePosition(id: string): Position {
  return {
    id,
    accountId: 'a',
    productId: 'p',
    customerName: 'Cliente',
    accountNumber: 'ACC',
    productName: 'Produto',
    ticker: 'TCK',
    quantity: 100,
    averagePrice: 10,
    grossAmount: 1000,
    updatedAt: '2026-06-10T00:00:00Z',
  };
}

describe('positions reducer', () => {
  it('load sets loading and clears error', () => {
    const state = reducer({ ...initialPositionsState, error: 'x' }, PositionsActions.load({ query: { page: 1, pageSize: 20 } }));
    expect(state.loading).toBe(true);
    expect(state.error).toBeNull();
  });

  it('loadSuccess populates items and total', () => {
    const result: Paged<Position> = { items: [makePosition('1')], total: 3, page: 1, pageSize: 20 };
    const state = reducer({ ...initialPositionsState, loading: true }, PositionsActions.loadSuccess({ result }));
    expect(state.items).toHaveLength(1);
    expect(state.total).toBe(3);
    expect(state.loading).toBe(false);
  });

  it('loadFailure sets error and stops loading', () => {
    const state = reducer({ ...initialPositionsState, loading: true }, PositionsActions.loadFailure({ error: 'boom' }));
    expect(state.error).toBe('boom');
    expect(state.loading).toBe(false);
  });
});
