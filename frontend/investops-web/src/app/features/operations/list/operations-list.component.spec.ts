import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { of } from 'rxjs';
import { LookupsApi } from '../../../core/api/lookups.api';
import { OperationListItem } from '../../../core/models';
import { OperationsActions } from '../state/operations.actions';
import { selectError, selectItems, selectLoading, selectTotal } from '../state/operations.feature';
import { OperationsListComponent } from './operations-list.component';

function makeItem(id: string, status: string): OperationListItem {
  return {
    id,
    externalId: `EXT-${id}`,
    accountId: 'a',
    productId: 'p',
    customerName: 'Cliente',
    accountNumber: 'ACC',
    productName: 'Produto',
    ticker: null,
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

const lookupsStub = { products: () => of([]), customers: () => of([]) };

describe('OperationsListComponent', () => {
  let store: MockStore;
  let fixture: ComponentFixture<OperationsListComponent>;

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [OperationsListComponent],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        provideMockStore(),
        { provide: LookupsApi, useValue: lookupsStub },
      ],
    });
    store = TestBed.inject(MockStore);
    store.overrideSelector(selectItems, []);
    store.overrideSelector(selectTotal, 0);
    store.overrideSelector(selectLoading, false);
    store.overrideSelector(selectError, null);
    fixture = TestBed.createComponent(OperationsListComponent);
  }

  it('renders one row per item', () => {
    setup();
    store.overrideSelector(selectItems, [makeItem('1', 'Processed'), makeItem('2', 'Failed')]);
    store.refreshState();
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('tr[mat-row]');
    expect(rows.length).toBe(2);
  });

  // Zoneless: fakeAsync/tick dependem de zone.js. Usamos fake timers do Jest,
  // que interceptam o setInterval do asyncScheduler do RxJS (debounceTime).
  it('dispatches load only after the debounce when searching', () => {
    jest.useFakeTimers();
    try {
      setup();
      fixture.detectChanges(); // ngOnInit: initial load + subscribes to search
      const dispatch = jest.spyOn(store, 'dispatch');

      fixture.componentInstance.searchControl.setValue('EXT-9');
      jest.advanceTimersByTime(299);
      expect(dispatch).not.toHaveBeenCalled();

      jest.advanceTimersByTime(1);
      expect(dispatch).toHaveBeenCalledTimes(1);
    } finally {
      jest.useRealTimers();
    }
  });

  it('shows the reprocess button only for reprocessable rows (Imported/Failed, not Processed)', () => {
    setup();
    store.overrideSelector(selectItems, [makeItem('1', 'Processed'), makeItem('2', 'Imported')]);
    store.refreshState();
    fixture.detectChanges();

    const replayIcons = Array.from(fixture.nativeElement.querySelectorAll('mat-icon')).filter(
      (icon) => (icon as HTMLElement).textContent?.trim() === 'replay',
    );
    expect(replayIcons.length).toBe(1);
  });

  it('dispatches reprocessOperation when the reprocess button is clicked', () => {
    setup();
    store.overrideSelector(selectItems, [makeItem('op-1', 'Imported')]);
    store.refreshState();
    fixture.detectChanges();
    const dispatch = jest.spyOn(store, 'dispatch');

    const replayButton = Array.from(fixture.nativeElement.querySelectorAll('button')).find((b) =>
      (b as HTMLElement).textContent?.includes('replay'),
    ) as HTMLButtonElement;
    replayButton.click();

    expect(dispatch).toHaveBeenCalledWith(OperationsActions.reprocessOperation({ id: 'op-1' }));
  });

  it('shows the error state when there is an error', () => {
    setup();
    store.overrideSelector(selectError, 'Falha ao carregar operações.');
    store.refreshState();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Falha ao carregar operações.');
  });
});
