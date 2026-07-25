import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { Divergence } from '../../core/models';
import { ReconciliationComponent } from './reconciliation.component';
import { ReconciliationActions } from './state/reconciliation.actions';
import { selectError, selectImportError, selectImporting, selectItems, selectLoading, selectTotal } from './state/reconciliation.feature';

function makeDivergence(id: string, status: string): Divergence {
  return {
    id,
    importId: 'imp',
    accountId: 'a',
    customerName: 'Cliente ' + id,
    accountNumber: 'ACC',
    productId: 'p',
    productName: 'Produto',
    ticker: 'TCK',
    internalQuantity: 100,
    custodianQuantity: 150,
    difference: 50,
    status,
    createdAt: '2026-06-10T00:00:00Z',
    resolvedAt: null,
  };
}

describe('ReconciliationComponent', () => {
  let store: MockStore;
  let fixture: ComponentFixture<ReconciliationComponent>;

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [ReconciliationComponent],
      providers: [provideNoopAnimations(), provideMockStore()],
    });
    store = TestBed.inject(MockStore);
    store.overrideSelector(selectItems, []);
    store.overrideSelector(selectTotal, 0);
    store.overrideSelector(selectLoading, false);
    store.overrideSelector(selectError, null);
    store.overrideSelector(selectImporting, false);
    store.overrideSelector(selectImportError, null);
    fixture = TestBed.createComponent(ReconciliationComponent);
  }

  it('renders one row per divergence', () => {
    setup();
    store.overrideSelector(selectItems, [makeDivergence('1', 'Pending'), makeDivergence('2', 'Resolved')]);
    store.refreshState();
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('tr[mat-row]');
    expect(rows.length).toBe(2);
  });

  it('dispatches load with the selected status when the filter changes', () => {
    setup();
    fixture.detectChanges();
    const dispatch = jest.spyOn(store, 'dispatch');

    fixture.componentInstance.statusFilter = 'Pending';
    fixture.componentInstance.onStatusChange();

    expect(dispatch).toHaveBeenCalledWith(
      ReconciliationActions.load({ query: { page: 1, pageSize: 20, sort: 'createdAt:desc', status: 'Pending' } }),
    );
  });

  it('shows Resolver/Ignorar actions only on Pending rows', () => {
    setup();
    store.overrideSelector(selectItems, [makeDivergence('1', 'Resolved'), makeDivergence('2', 'Pending')]);
    store.refreshState();
    fixture.detectChanges();

    const resolveButtons = Array.from(fixture.nativeElement.querySelectorAll('button')).filter(
      (b) => (b as HTMLElement).textContent?.trim() === 'Resolver',
    );
    const ignoreButtons = Array.from(fixture.nativeElement.querySelectorAll('button')).filter(
      (b) => (b as HTMLElement).textContent?.trim() === 'Ignorar',
    );
    expect(resolveButtons.length).toBe(1);
    expect(ignoreButtons.length).toBe(1);
  });

  it('dispatches resolve when the Resolver button is clicked', () => {
    setup();
    store.overrideSelector(selectItems, [makeDivergence('div-1', 'Pending')]);
    store.refreshState();
    fixture.detectChanges();
    const dispatch = jest.spyOn(store, 'dispatch');

    const resolveButton = Array.from(fixture.nativeElement.querySelectorAll('button')).find(
      (b) => (b as HTMLElement).textContent?.trim() === 'Resolver',
    ) as HTMLButtonElement;
    resolveButton.click();

    expect(dispatch).toHaveBeenCalledWith(ReconciliationActions.resolve({ id: 'div-1' }));
  });

  it('dispatches import with the file and formatted reference date on submit', () => {
    setup();
    fixture.detectChanges();
    const dispatch = jest.spyOn(store, 'dispatch');
    const file = new File(['account_id,product_id,quantity'], 'extrato.csv', { type: 'text/csv' });

    fixture.componentInstance.selectedFile.set(file);
    fixture.componentInstance.importForm.setValue({ source: 'Custodiante XPTO', referenceDate: new Date(2026, 5, 10) });
    fixture.componentInstance.submitImport();

    expect(dispatch).toHaveBeenCalledWith(
      ReconciliationActions.import({ source: 'Custodiante XPTO', referenceDate: '2026-06-10', file }),
    );
  });

  it('does not dispatch import when no file is selected', () => {
    setup();
    fixture.detectChanges();
    const dispatch = jest.spyOn(store, 'dispatch');

    fixture.componentInstance.importForm.setValue({ source: 'Custodiante XPTO', referenceDate: new Date(2026, 5, 10) });
    fixture.componentInstance.submitImport();

    expect(dispatch).not.toHaveBeenCalled();
  });

  it('shows the error state when there is an error', () => {
    setup();
    store.overrideSelector(selectError, 'Falha ao carregar divergências.');
    store.refreshState();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Falha ao carregar divergências.');
  });
});
