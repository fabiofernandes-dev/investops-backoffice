import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { Product } from '../../core/models';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { ErrorStateComponent } from '../../shared/error-state/error-state.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip/status-chip.component';
import { ProductsActions } from './state/products.actions';
import { selectError, selectItems, selectLoading } from './state/products.feature';

@Component({
  selector: 'app-products-list',
  standalone: true,
  imports: [
    RouterLink,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    PageHeaderComponent,
    StatusChipComponent,
    ErrorStateComponent,
    EmptyStateComponent,
  ],
  templateUrl: './products-list.component.html',
  styleUrl: './products-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductsListComponent implements OnInit {
  private readonly store = inject(Store);

  readonly items = this.store.selectSignal(selectItems);
  readonly loading = this.store.selectSignal(selectLoading);
  readonly error = this.store.selectSignal(selectError);

  readonly displayedColumns = ['name', 'ticker', 'productType', 'riskLevel', 'liquidityDays', 'isActive', 'actions'];

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.store.dispatch(ProductsActions.load());
  }

  remove(product: Product): void {
    if (confirm(`Excluir o produto "${product.name}"?`)) {
      this.store.dispatch(ProductsActions.remove({ id: product.id }));
    }
  }
}
