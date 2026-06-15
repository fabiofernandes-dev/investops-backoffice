import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Observable, of } from 'rxjs';
import { LookupsApi } from '../../core/api/lookups.api';
import { QuotesApi } from '../../core/api/quotes.api';
import { Quote } from '../../core/models';
import { QuotesComponent } from './quotes.component';

const products = [{ id: 'p1', name: 'Petrobras PN', ticker: 'PETR4' }];

function setup(stream: Observable<Quote>): ComponentFixture<QuotesComponent> {
  TestBed.configureTestingModule({
    imports: [QuotesComponent],
    providers: [
      provideNoopAnimations(),
      { provide: LookupsApi, useValue: { products: () => of(products) } },
      { provide: QuotesApi, useValue: { stream: () => stream } },
    ],
  });
  const fixture = TestBed.createComponent(QuotesComponent);
  fixture.detectChanges();
  return fixture;
}

describe('QuotesComponent', () => {
  it('renders a row per product with the live price', () => {
    const fixture = setup(of({ productId: 'p1', price: 38.42, asOf: '2026-06-14T12:00:00Z' }));
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Petrobras PN');
    expect(text).toContain('38.42');
  });

  it('shows an up arrow when the price rises', () => {
    const fixture = setup(
      of(
        { productId: 'p1', price: 10, asOf: '2026-06-14T12:00:00Z' },
        { productId: 'p1', price: 11, asOf: '2026-06-14T12:00:01Z' },
      ),
    );
    fixture.detectChanges();

    const icons = Array.from(fixture.nativeElement.querySelectorAll('mat-icon')).map((i) =>
      (i as HTMLElement).textContent?.trim(),
    );
    expect(icons).toContain('arrow_upward');
  });
});
