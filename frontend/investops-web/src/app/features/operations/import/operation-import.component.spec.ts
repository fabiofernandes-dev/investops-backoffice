import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { of } from 'rxjs';
import { LookupsApi } from '../../../core/api/lookups.api';
import { OperationsActions } from '../state/operations.actions';
import { selectImportError, selectImporting } from '../state/operations.feature';
import { OperationImportComponent } from './operation-import.component';

const lookupsStub = { accounts: () => of([]), products: () => of([]), customers: () => of([]) };

describe('OperationImportComponent', () => {
  let store: MockStore;
  let fixture: ComponentFixture<OperationImportComponent>;

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [OperationImportComponent],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        provideMockStore(),
        { provide: LookupsApi, useValue: lookupsStub },
      ],
    });
    store = TestBed.inject(MockStore);
    store.overrideSelector(selectImporting, false);
    store.overrideSelector(selectImportError, null);
    fixture = TestBed.createComponent(OperationImportComponent);
    fixture.detectChanges();
  }

  it('does not dispatch import when the form is invalid', () => {
    setup();
    const dispatch = jest.spyOn(store, 'dispatch');

    fixture.componentInstance.submit();

    expect(dispatch).not.toHaveBeenCalled();
  });

  it('dispatches importOperation with the form values when valid', () => {
    setup();
    const dispatch = jest.spyOn(store, 'dispatch');
    fixture.componentInstance.form.setValue({
      externalId: 'EXT-1',
      accountId: 'acc-1',
      productId: 'prod-1',
      operationType: 'Buy',
      quantity: 10,
      unitPrice: 5,
      operationDate: '2026-06-13',
    });

    fixture.componentInstance.submit();

    expect(dispatch).toHaveBeenCalledWith(
      OperationsActions.importOperation({
        request: {
          externalId: 'EXT-1',
          accountId: 'acc-1',
          productId: 'prod-1',
          operationType: 'Buy',
          quantity: 10,
          unitPrice: 5,
          operationDate: '2026-06-13',
        },
      }),
    );
  });

  it('shows the import error', () => {
    setup();
    store.overrideSelector(selectImportError, 'Referência inválida (conta ou produto).');
    store.refreshState();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Referência inválida');
  });
});
