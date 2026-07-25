import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { ProductFormComponent } from './product-form.component';
import { ProductsActions } from './state/products.actions';
import { selectItems, selectSaveError, selectSaving } from './state/products.feature';

describe('ProductFormComponent', () => {
  let store: MockStore;
  let fixture: ComponentFixture<ProductFormComponent>;

  function setup(id: string | null): void {
    TestBed.configureTestingModule({
      imports: [ProductFormComponent],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        provideMockStore(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } },
      ],
    });
    store = TestBed.inject(MockStore);
    store.overrideSelector(selectItems, []);
    store.overrideSelector(selectSaving, false);
    store.overrideSelector(selectSaveError, null);
    fixture = TestBed.createComponent(ProductFormComponent);
    fixture.detectChanges();
  }

  it('does not dispatch save when the name is empty', () => {
    setup(null);
    const dispatch = jest.spyOn(store, 'dispatch');

    fixture.componentInstance.form.patchValue({ name: '', riskLevel: 2, liquidityDays: 0 });
    fixture.componentInstance.submit();

    expect(dispatch).not.toHaveBeenCalled();
  });

  it('does not dispatch save when the risk level is out of the 1-5 range', () => {
    setup(null);
    const dispatch = jest.spyOn(store, 'dispatch');

    fixture.componentInstance.form.patchValue({ name: 'CDB', riskLevel: 6, liquidityDays: 0 });
    expect(fixture.componentInstance.form.invalid).toBe(true);

    fixture.componentInstance.submit();
    expect(dispatch).not.toHaveBeenCalled();
  });

  it('dispatches save without an id when creating a new product', () => {
    setup(null);
    const dispatch = jest.spyOn(store, 'dispatch');

    fixture.componentInstance.form.patchValue({
      productType: 'Stock',
      name: 'PETR4',
      ticker: 'PETR4',
      riskLevel: 4,
      liquidityDays: 2,
    });
    fixture.componentInstance.submit();

    expect(dispatch).toHaveBeenCalledWith(
      ProductsActions.save({
        request: { productType: 'Stock', name: 'PETR4', ticker: 'PETR4', riskLevel: 4, liquidityDays: 2 },
      }),
    );
  });

  it('dispatches save with the id when editing an existing product', () => {
    setup('p-1');
    const dispatch = jest.spyOn(store, 'dispatch');

    fixture.componentInstance.form.patchValue({
      productType: 'FixedIncome',
      name: 'Tesouro',
      ticker: null,
      riskLevel: 1,
      liquidityDays: 30,
      isActive: true,
    });
    fixture.componentInstance.submit();

    expect(dispatch).toHaveBeenCalledWith(
      ProductsActions.save({
        id: 'p-1',
        request: { productType: 'FixedIncome', name: 'Tesouro', ticker: null, riskLevel: 1, liquidityDays: 30, isActive: true },
      }),
    );
  });

  it('shows the duplicate ticker error from the store', () => {
    setup(null);
    store.overrideSelector(selectSaveError, 'Ticker já existe.');
    store.refreshState();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Ticker já existe.');
  });
});
