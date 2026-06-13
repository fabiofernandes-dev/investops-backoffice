import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { LookupsApi } from '../../../core/api/lookups.api';
import { CustomerLookup, OperationsQuery, ProductLookup } from '../../../core/models';
import { OperationsActions } from '../state/operations.actions';
import { selectError, selectItems, selectLoading, selectTotal } from '../state/operations.feature';

@Component({
  selector: 'app-operations-list',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink,
    MatTableModule,
    MatSortModule,
    MatPaginatorModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatTooltipModule,
  ],
  templateUrl: './operations-list.component.html',
  styleUrl: './operations-list.component.scss',
})
export class OperationsListComponent implements OnInit {
  private readonly store = inject(Store);
  private readonly lookups = inject(LookupsApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly items = this.store.selectSignal(selectItems);
  readonly total = this.store.selectSignal(selectTotal);
  readonly loading = this.store.selectSignal(selectLoading);
  readonly error = this.store.selectSignal(selectError);

  readonly products = signal<ProductLookup[]>([]);
  readonly customers = signal<CustomerLookup[]>([]);

  readonly statuses = ['Imported', 'Validated', 'Processed', 'Rejected', 'Failed'];
  readonly displayedColumns = [
    'externalId',
    'customerName',
    'accountNumber',
    'productName',
    'operationType',
    'quantity',
    'grossValue',
    'status',
    'operationDate',
    'settlementDate',
    'actions',
  ];

  readonly searchControl = new FormControl('');
  status = '';
  productId = '';
  customerId = '';
  pageIndex = 0;
  pageSize = 20;
  sortActive = 'operationDate';
  sortDirection: 'asc' | 'desc' = 'desc';

  ngOnInit(): void {
    this.lookups.products().subscribe((p) => this.products.set(p));
    this.lookups.customers().subscribe((c) => this.customers.set(c));

    this.searchControl.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.pageIndex = 0;
        this.reload();
      });

    this.reload();
  }

  reload(): void {
    const query: OperationsQuery = {
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
      sort: `${this.sortActive}:${this.sortDirection}`,
      status: this.status || undefined,
      productId: this.productId || undefined,
      customerId: this.customerId || undefined,
      externalId: this.searchControl.value?.trim() || undefined,
    };
    this.store.dispatch(OperationsActions.load({ query }));
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.reload();
  }

  onSort(sort: Sort): void {
    if (sort.direction) {
      this.sortActive = sort.active;
      this.sortDirection = sort.direction;
    } else {
      this.sortActive = 'operationDate';
      this.sortDirection = 'desc';
    }
    this.pageIndex = 0;
    this.reload();
  }

  onFilterChange(): void {
    this.pageIndex = 0;
    this.reload();
  }

  statusColor(status: string): string {
    switch (status) {
      case 'Processed':
        return '#2e7d32';
      case 'Rejected':
        return '#c62828';
      case 'Failed':
        return '#ef6c00';
      case 'Validated':
        return '#1565c0';
      default:
        return '#616161';
    }
  }

  isFailed(status: string): boolean {
    return status === 'Failed';
  }
}
