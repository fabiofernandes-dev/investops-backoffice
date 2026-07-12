import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { LookupsApi } from '../../core/api/lookups.api';
import { QuotesApi } from '../../core/api/quotes.api';
import { ProductLookup } from '../../core/models';
import { ErrorStateComponent } from '../../shared/error-state/error-state.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

interface QuoteState {
  price: number;
  prev: number | null;
  asOf: string;
}

@Component({
  selector: 'app-quotes',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatIconModule, PageHeaderComponent, ErrorStateComponent],
  templateUrl: './quotes.component.html',
  styleUrl: './quotes.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuotesComponent implements OnInit {
  private readonly lookups = inject(LookupsApi);
  private readonly quotesApi = inject(QuotesApi);
  private readonly destroyRef = inject(DestroyRef);

  private readonly products = signal<ProductLookup[]>([]);
  private readonly quotes = signal<Record<string, QuoteState>>({});
  readonly error = signal<string | null>(null);

  readonly displayedColumns = ['name', 'ticker', 'price', 'change', 'asOf'];

  readonly rows = computed(() => {
    const q = this.quotes();
    return this.products().map((p) => ({
      id: p.id,
      name: p.name,
      ticker: p.ticker,
      price: q[p.id]?.price ?? null,
      prev: q[p.id]?.prev ?? null,
      asOf: q[p.id]?.asOf ?? null,
    }));
  });

  ngOnInit(): void {
    this.lookups
      .products()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((products) => {
        this.products.set(products);
        if (products.length > 0) {
          this.startStream(products.map((p) => p.id));
        }
      });
  }

  private startStream(ids: string[]): void {
    this.quotesApi
      .stream(ids)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (quote) =>
          this.quotes.update((map) => ({
            ...map,
            [quote.productId]: { price: quote.price, prev: map[quote.productId]?.price ?? null, asOf: quote.asOf },
          })),
        error: () => this.error.set('Falha no stream de cotações.'),
      });
  }

  direction(row: { price: number | null; prev: number | null }): 'up' | 'down' | 'flat' | 'none' {
    if (row.price === null || row.prev === null) {
      return 'none';
    }
    if (row.price > row.prev) {
      return 'up';
    }
    if (row.price < row.prev) {
      return 'down';
    }
    return 'flat';
  }
}
