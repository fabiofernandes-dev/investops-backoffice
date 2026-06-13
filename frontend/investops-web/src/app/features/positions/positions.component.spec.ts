import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { Position } from '../../core/models';
import { PositionsComponent } from './positions.component';
import { selectError, selectItems, selectLoading, selectTotal } from './state/positions.feature';

function makePosition(id: string): Position {
  return {
    id,
    accountId: 'a',
    productId: 'p',
    customerName: 'Cliente ' + id,
    accountNumber: 'ACC',
    productName: 'Produto',
    ticker: 'TCK',
    quantity: 100,
    averagePrice: 10,
    grossAmount: 1000,
    updatedAt: '2026-06-10T00:00:00Z',
  };
}

describe('PositionsComponent', () => {
  let store: MockStore;
  let fixture: ComponentFixture<PositionsComponent>;

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [PositionsComponent],
      providers: [provideNoopAnimations(), provideMockStore()],
    });
    store = TestBed.inject(MockStore);
    store.overrideSelector(selectItems, []);
    store.overrideSelector(selectTotal, 0);
    store.overrideSelector(selectLoading, false);
    store.overrideSelector(selectError, null);
    fixture = TestBed.createComponent(PositionsComponent);
  }

  it('renders one row per position', () => {
    setup();
    store.overrideSelector(selectItems, [makePosition('1'), makePosition('2')]);
    store.refreshState();
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('tr[mat-row]');
    expect(rows.length).toBe(2);
  });

  it('shows the error state when there is an error', () => {
    setup();
    store.overrideSelector(selectError, 'Falha ao carregar posições.');
    store.refreshState();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Falha ao carregar posições.');
  });
});
