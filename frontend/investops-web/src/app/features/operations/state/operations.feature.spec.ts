import { OperationListItem, Paged } from '../../../core/models';
import { OperationsActions } from './operations.actions';
import { initialOperationsState, operationsFeature } from './operations.feature';

const reducer = operationsFeature.reducer;

function makeItem(id: string, status = 'Processed'): OperationListItem {
  return {
    id,
    externalId: `EXT-${id}`,
    accountId: 'a',
    productId: 'p',
    customerName: 'Cliente',
    accountNumber: 'ACC',
    productName: 'Produto',
    ticker: 'TCK',
    operationType: 'Buy',
    quantity: 1,
    unitPrice: 1,
    grossValue: 1,
    netValue: 1,
    operationDate: '2026-06-10',
    settlementDate: '2026-06-12',
    status,
    rejectionReason: null,
  };
}

describe('operations reducer', () => {
  it('load sets loading and clears error', () => {
    const state = reducer({ ...initialOperationsState, error: 'x' }, OperationsActions.load({ query: { page: 1, pageSize: 20 } }));
    expect(state.loading).toBe(true);
    expect(state.error).toBeNull();
  });

  it('loadSuccess populates items and total', () => {
    const result: Paged<OperationListItem> = { items: [makeItem('1')], total: 7, page: 1, pageSize: 20 };
    const state = reducer({ ...initialOperationsState, loading: true }, OperationsActions.loadSuccess({ result }));
    expect(state.items).toHaveLength(1);
    expect(state.total).toBe(7);
    expect(state.loading).toBe(false);
  });

  it('loadFailure sets error and stops loading', () => {
    const state = reducer({ ...initialOperationsState, loading: true }, OperationsActions.loadFailure({ error: 'boom' }));
    expect(state.error).toBe('boom');
    expect(state.loading).toBe(false);
  });

  it('loadDetailSuccess populates detail/events/audit', () => {
    const state = reducer(
      initialOperationsState,
      OperationsActions.loadDetailSuccess({
        detail: { id: '1' } as never,
        events: [{ eventType: 'X' } as never],
        audit: [],
      }),
    );
    expect(state.detail).not.toBeNull();
    expect(state.events).toHaveLength(1);
    expect(state.detailLoading).toBe(false);
  });
});
