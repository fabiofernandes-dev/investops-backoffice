import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { Product } from '../../core/models';
import { ProductsListComponent } from './products-list.component';
import { ProductsActions } from './state/products.actions';
import { selectError, selectItems, selectLoading } from './state/products.feature';

function makeProduct(id: string, isActive = true): Product {
  return {
    id,
    productType: 'Stock',
    name: 'Produto ' + id,
    ticker: 'TCK' + id,
    riskLevel: 3,
    liquidityDays: 1,
    isActive,
  };
}

describe('ProductsListComponent', () => {
  let store: MockStore;
  let fixture: ComponentFixture<ProductsListComponent>;

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [ProductsListComponent],
      providers: [provideNoopAnimations(), provideRouter([]), provideMockStore()],
    });
    store = TestBed.inject(MockStore);
    store.overrideSelector(selectItems, []);
    store.overrideSelector(selectLoading, false);
    store.overrideSelector(selectError, null);
    fixture = TestBed.createComponent(ProductsListComponent);
  }

  it('renders one row per product', () => {
    setup();
    store.overrideSelector(selectItems, [makeProduct('1'), makeProduct('2')]);
    store.refreshState();
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('tr[mat-row]');
    expect(rows.length).toBe(2);
  });

  it('dispatches remove when Excluir is confirmed', () => {
    setup();
    store.overrideSelector(selectItems, [makeProduct('prod-1')]);
    store.refreshState();
    fixture.detectChanges();
    const dispatch = jest.spyOn(store, 'dispatch');
    jest.spyOn(window, 'confirm').mockReturnValue(true);

    const excluir = Array.from(fixture.nativeElement.querySelectorAll('button')).find(
      (b) => (b as HTMLElement).textContent?.trim() === 'Excluir',
    ) as HTMLButtonElement;
    excluir.click();

    expect(dispatch).toHaveBeenCalledWith(ProductsActions.remove({ id: 'prod-1' }));
  });

  it('does not dispatch remove when the confirmation is cancelled', () => {
    setup();
    store.overrideSelector(selectItems, [makeProduct('prod-1')]);
    store.refreshState();
    fixture.detectChanges();
    const dispatch = jest.spyOn(store, 'dispatch');
    jest.spyOn(window, 'confirm').mockReturnValue(false);

    const excluir = Array.from(fixture.nativeElement.querySelectorAll('button')).find(
      (b) => (b as HTMLElement).textContent?.trim() === 'Excluir',
    ) as HTMLButtonElement;
    excluir.click();

    expect(dispatch).not.toHaveBeenCalledWith(ProductsActions.remove({ id: 'prod-1' }));
  });

  it('shows the error state when there is an error', () => {
    setup();
    store.overrideSelector(selectError, 'Falha ao carregar produtos.');
    store.refreshState();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Falha ao carregar produtos.');
  });
});
