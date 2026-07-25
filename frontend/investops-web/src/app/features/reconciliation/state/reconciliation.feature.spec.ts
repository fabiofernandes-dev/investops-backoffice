import { Divergence, ImportReconciliationResult, Paged } from '../../../core/models';
import { ReconciliationActions } from './reconciliation.actions';
import { initialReconciliationState, reconciliationFeature } from './reconciliation.feature';

const reducer = reconciliationFeature.reducer;

function makeDivergence(id: string): Divergence {
  return {
    id,
    importId: 'imp',
    accountId: 'a',
    customerName: 'Cliente',
    accountNumber: 'ACC',
    productId: 'p',
    productName: 'Produto',
    ticker: 'TCK',
    internalQuantity: 100,
    custodianQuantity: 150,
    difference: 50,
    status: 'Pending',
    createdAt: '2026-06-10T00:00:00Z',
    resolvedAt: null,
  };
}

function makeResult(): ImportReconciliationResult {
  return {
    importId: 'imp-1',
    source: 'Custodiante XPTO',
    referenceDate: '2026-06-10',
    rowsProcessed: 3,
    matchedCount: 2,
    divergenceCount: 1,
  };
}

describe('reconciliation reducer', () => {
  it('load sets loading and clears error', () => {
    const state = reducer({ ...initialReconciliationState, error: 'x' }, ReconciliationActions.load({ query: { page: 1, pageSize: 20 } }));
    expect(state.loading).toBe(true);
    expect(state.error).toBeNull();
  });

  it('loadSuccess populates items and total', () => {
    const result: Paged<Divergence> = { items: [makeDivergence('1')], total: 5, page: 1, pageSize: 20 };
    const state = reducer({ ...initialReconciliationState, loading: true }, ReconciliationActions.loadSuccess({ result }));
    expect(state.items).toHaveLength(1);
    expect(state.total).toBe(5);
    expect(state.loading).toBe(false);
  });

  it('loadFailure sets error and stops loading', () => {
    const state = reducer({ ...initialReconciliationState, loading: true }, ReconciliationActions.loadFailure({ error: 'boom' }));
    expect(state.error).toBe('boom');
    expect(state.loading).toBe(false);
  });

  it('import sets importing and clears importError', () => {
    const state = reducer(
      { ...initialReconciliationState, importError: 'x' },
      ReconciliationActions.import({ source: 'S', referenceDate: '2026-06-10', file: new File([''], 'x.csv') }),
    );
    expect(state.importing).toBe(true);
    expect(state.importError).toBeNull();
  });

  it('importSuccess stores lastImport and stops importing', () => {
    const result = makeResult();
    const state = reducer({ ...initialReconciliationState, importing: true }, ReconciliationActions.importSuccess({ result }));
    expect(state.importing).toBe(false);
    expect(state.lastImport).toEqual(result);
  });

  it('importFailure sets importError and stops importing', () => {
    const state = reducer({ ...initialReconciliationState, importing: true }, ReconciliationActions.importFailure({ error: 'CSV inválido.' }));
    expect(state.importing).toBe(false);
    expect(state.importError).toBe('CSV inválido.');
  });
});
