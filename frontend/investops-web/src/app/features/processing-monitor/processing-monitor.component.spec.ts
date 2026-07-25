import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { ProcessingEventItem } from '../../core/models';
import { ProcessingMonitorComponent } from './processing-monitor.component';
import { ProcessingEventsActions } from './state/processing-events.actions';
import { selectError, selectItems, selectLoading, selectTotal } from './state/processing-events.feature';

function makeEvent(id: string, status = 'Processed'): ProcessingEventItem {
  return {
    id,
    operationId: 'op-' + id,
    externalId: 'EXT-' + id,
    eventType: 'OperationProcessed',
    status,
    attemptCount: status === 'Failed' ? 3 : 0,
    errorMessage: status === 'Failed' ? 'timeout na cotação' : null,
    createdAt: '2026-06-10T00:00:00Z',
    processedAt: '2026-06-10T00:00:01Z',
  };
}

describe('ProcessingMonitorComponent', () => {
  let store: MockStore;
  let fixture: ComponentFixture<ProcessingMonitorComponent>;

  // App é zoneless (sem zone.js/testing → sem fakeAsync); jest fake timers dirigem
  // o timer(0, 5000) do polling em tempo virtual e evitam intervals reais vazando.
  beforeEach(() => jest.useFakeTimers());
  afterEach(() => jest.useRealTimers());

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [ProcessingMonitorComponent],
      providers: [provideNoopAnimations(), provideRouter([]), provideMockStore()],
    });
    store = TestBed.inject(MockStore);
    store.overrideSelector(selectItems, []);
    store.overrideSelector(selectTotal, 0);
    store.overrideSelector(selectLoading, false);
    store.overrideSelector(selectError, null);
    fixture = TestBed.createComponent(ProcessingMonitorComponent);
  }

  it('renders one row per processing event', () => {
    setup();
    store.overrideSelector(selectItems, [makeEvent('1'), makeEvent('2', 'Failed')]);
    store.refreshState();
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('tr[mat-row]');
    expect(rows.length).toBe(2);
  });

  it('links the operation column to the operation detail route', () => {
    setup();
    store.overrideSelector(selectItems, [makeEvent('1')]);
    store.refreshState();
    fixture.detectChanges();

    const link = fixture.nativeElement.querySelector('tr[mat-row] a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/operations/op-1');
    expect(link.textContent?.trim()).toBe('EXT-1');
  });

  it('dispatches load with the selected status when the filter changes', () => {
    setup();
    fixture.detectChanges();
    const dispatch = jest.spyOn(store, 'dispatch');

    fixture.componentInstance.statusFilter = 'Failed';
    fixture.componentInstance.onStatusChange();

    expect(dispatch).toHaveBeenCalledWith(
      ProcessingEventsActions.load({ query: { page: 1, pageSize: 20, sort: 'createdAt:desc', status: 'Failed' }, silent: false }),
    );
  });

  it('polls a silent load while auto-refresh is on and stops when off', () => {
    setup();
    fixture.detectChanges();
    const dispatch = jest.spyOn(store, 'dispatch');

    fixture.componentInstance.autoRefresh.set(true);
    jest.advanceTimersByTime(5000);
    expect(dispatch).toHaveBeenCalledWith(expect.objectContaining({ silent: true }));

    dispatch.mockClear();
    fixture.componentInstance.autoRefresh.set(false);
    jest.advanceTimersByTime(10000);
    expect(dispatch).not.toHaveBeenCalled();
  });

  it('shows the error state when there is an error', () => {
    setup();
    store.overrideSelector(selectError, 'Falha ao carregar os eventos de processamento.');
    store.refreshState();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Falha ao carregar os eventos de processamento.');
  });
});
