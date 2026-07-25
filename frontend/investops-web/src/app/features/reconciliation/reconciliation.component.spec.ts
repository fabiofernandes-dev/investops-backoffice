import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Actions } from '@ngrx/effects';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { Subject } from 'rxjs';
import { Action } from '@ngrx/store';
import { Divergence, ImportReconciliationResult } from '../../core/models';
import { ReconciliationComponent } from './reconciliation.component';
import { ReconciliationActions } from './state/reconciliation.actions';
import { selectError, selectImportError, selectImporting, selectItems, selectLastImport, selectLoading, selectTotal } from './state/reconciliation.feature';

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
  let actions$: Subject<Action>;

  function setup(): void {
    actions$ = new Subject<Action>();
    TestBed.configureTestingModule({
      imports: [ReconciliationComponent],
      providers: [provideNoopAnimations(), provideMockStore(), { provide: Actions, useValue: actions$ }],
    });
    store = TestBed.inject(MockStore);
    store.overrideSelector(selectItems, []);
    store.overrideSelector(selectTotal, 0);
    store.overrideSelector(selectLoading, false);
    store.overrideSelector(selectError, null);
    store.overrideSelector(selectImporting, false);
    store.overrideSelector(selectImportError, null);
    store.overrideSelector(selectLastImport, null);
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

  it('clears the form, file and filter after a successful import', () => {
    setup();
    fixture.detectChanges();
    fixture.componentInstance.selectedFile.set(new File(['x'], 'extrato.csv'));
    fixture.componentInstance.importForm.setValue({ source: 'Custodiante XPTO', referenceDate: new Date(2026, 5, 10) });
    fixture.componentInstance.statusFilter = 'Resolved';

    actions$.next(ReconciliationActions.importSuccess({ result: makeResult() }));

    expect(fixture.componentInstance.selectedFile()).toBeNull();
    expect(fixture.componentInstance.importForm.controls.source.value).toBe('');
    expect(fixture.componentInstance.importForm.controls.referenceDate.value).toBeNull();
    expect(fixture.componentInstance.statusFilter).toBe('');
    expect(fixture.componentInstance.pageIndex).toBe(0);
  });

  it('shows the error state when there is an error', () => {
    setup();
    store.overrideSelector(selectError, 'Falha ao carregar divergências.');
    store.refreshState();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Falha ao carregar divergências.');
  });
});
