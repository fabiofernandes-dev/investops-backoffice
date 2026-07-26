import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { timer } from 'rxjs';
import { ProcessingEventsQuery } from '../../core/models';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { ErrorStateComponent } from '../../shared/error-state/error-state.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip/status-chip.component';
import { ProcessingEventsActions } from './state/processing-events.actions';
import { selectError, selectItems, selectLoading, selectTotal } from './state/processing-events.feature';

@Component({
  selector: 'app-processing-monitor',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatTableModule,
    MatSortModule,
    MatPaginatorModule,
    MatFormFieldModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatTooltipModule,
    MatSlideToggleModule,
    PageHeaderComponent,
    StatusChipComponent,
    ErrorStateComponent,
    EmptyStateComponent,
  ],
  templateUrl: './processing-monitor.component.html',
  styleUrl: './processing-monitor.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProcessingMonitorComponent implements OnInit {
  private readonly store = inject(Store);

  readonly items = this.store.selectSignal(selectItems);
  readonly total = this.store.selectSignal(selectTotal);
  readonly loading = this.store.selectSignal(selectLoading);
  readonly error = this.store.selectSignal(selectError);

  readonly statuses = ['Processed', 'Rejected', 'Failed'];
  readonly displayedColumns = ['eventType', 'externalId', 'status', 'attemptCount', 'errorMessage', 'createdAt', 'processedAt'];

  // Estado do auto-refresh persistido entre sessões (localStorage).
  readonly autoRefresh = signal(localStorage.getItem('pm.autoRefresh') === 'true');

  statusFilter = '';
  pageIndex = 0;
  pageSize = 20;
  sortActive = 'createdAt';
  sortDirection: 'asc' | 'desc' = 'desc';

  constructor() {
    effect(() => localStorage.setItem('pm.autoRefresh', String(this.autoRefresh())));

    timer(0, 5000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        if (this.autoRefresh() && !document.hidden) {
          this.reload(true);
        }
      });
  }

  ngOnInit(): void {
    this.reload();
  }

  reload(silent = false): void {
    const query: ProcessingEventsQuery = {
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
      sort: `${this.sortActive}:${this.sortDirection}`,
      status: this.statusFilter || undefined,
    };
    this.store.dispatch(ProcessingEventsActions.load({ query, silent }));
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
      this.sortActive = 'createdAt';
      this.sortDirection = 'desc';
    }
    this.pageIndex = 0;
    this.reload();
  }

  onStatusChange(): void {
    this.pageIndex = 0;
    this.reload();
  }
}
