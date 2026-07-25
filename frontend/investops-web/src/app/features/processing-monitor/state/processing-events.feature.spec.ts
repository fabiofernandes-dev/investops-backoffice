import { Paged, ProcessingEventItem } from '../../../core/models';
import { ProcessingEventsActions } from './processing-events.actions';
import { initialProcessingEventsState, processingEventsFeature } from './processing-events.feature';

const reducer = processingEventsFeature.reducer;

function makeEvent(id: string): ProcessingEventItem {
  return {
    id,
    operationId: 'op-' + id,
    externalId: 'EXT-' + id,
    eventType: 'OperationProcessed',
    status: 'Processed',
    attemptCount: 0,
    errorMessage: null,
    createdAt: '2026-06-10T00:00:00Z',
    processedAt: '2026-06-10T00:00:01Z',
  };
}

describe('processing events reducer', () => {
  it('load with silent false sets loading and clears error', () => {
    const state = reducer(
      { ...initialProcessingEventsState, error: 'x' },
      ProcessingEventsActions.load({ query: { page: 1, pageSize: 20 }, silent: false }),
    );
    expect(state.loading).toBe(true);
    expect(state.error).toBeNull();
  });

  it('load with silent true keeps loading false (no flicker on auto-refresh)', () => {
    const state = reducer(
      initialProcessingEventsState,
      ProcessingEventsActions.load({ query: { page: 1, pageSize: 20 }, silent: true }),
    );
    expect(state.loading).toBe(false);
    expect(state.error).toBeNull();
  });

  it('load without silent sets loading (defaults to non-silent)', () => {
    const state = reducer(initialProcessingEventsState, ProcessingEventsActions.load({ query: { page: 1, pageSize: 20 } }));
    expect(state.loading).toBe(true);
  });

  it('loadSuccess populates items and total', () => {
    const result: Paged<ProcessingEventItem> = { items: [makeEvent('1')], total: 5, page: 1, pageSize: 20 };
    const state = reducer({ ...initialProcessingEventsState, loading: true }, ProcessingEventsActions.loadSuccess({ result }));
    expect(state.items).toHaveLength(1);
    expect(state.total).toBe(5);
    expect(state.loading).toBe(false);
  });

  it('loadFailure sets error and stops loading', () => {
    const state = reducer({ ...initialProcessingEventsState, loading: true }, ProcessingEventsActions.loadFailure({ error: 'boom' }));
    expect(state.error).toBe('boom');
    expect(state.loading).toBe(false);
  });
});
