import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { Store } from '@ngrx/store';
import { PositionsQuery } from '../../core/models';
import { PositionsActions } from './state/positions.actions';
import { selectError, selectItems, selectLoading, selectTotal } from './state/positions.feature';

@Component({
  selector: 'app-positions',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatSortModule, MatPaginatorModule, MatButtonModule],
  templateUrl: './positions.component.html',
  styleUrl: './positions.component.scss',
})
export class PositionsComponent implements OnInit {
  private readonly store = inject(Store);

  readonly items = this.store.selectSignal(selectItems);
  readonly total = this.store.selectSignal(selectTotal);
  readonly loading = this.store.selectSignal(selectLoading);
  readonly error = this.store.selectSignal(selectError);

  readonly displayedColumns = ['customerName', 'accountNumber', 'productName', 'quantity', 'averagePrice', 'grossAmount', 'updatedAt'];

  pageIndex = 0;
  pageSize = 20;
  sortActive = 'updatedAt';
  sortDirection: 'asc' | 'desc' = 'desc';

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    const query: PositionsQuery = {
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
      sort: `${this.sortActive}:${this.sortDirection}`,
    };
    this.store.dispatch(PositionsActions.load({ query }));
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
      this.sortActive = 'updatedAt';
      this.sortDirection = 'desc';
    }
    this.pageIndex = 0;
    this.reload();
  }
}
