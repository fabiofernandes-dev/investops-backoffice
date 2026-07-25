import { CommonModule, formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Store } from '@ngrx/store';
import { DivergencesQuery } from '../../core/models';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { ErrorStateComponent } from '../../shared/error-state/error-state.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip/status-chip.component';
import { ReconciliationActions } from './state/reconciliation.actions';
import { selectError, selectImportError, selectImporting, selectItems, selectLoading, selectTotal } from './state/reconciliation.feature';

@Component({
  selector: 'app-reconciliation',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatTableModule,
    MatSortModule,
    MatPaginatorModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatTooltipModule,
    MatCardModule,
    MatDatepickerModule,
    PageHeaderComponent,
    StatusChipComponent,
    ErrorStateComponent,
    EmptyStateComponent,
  ],
  providers: [provideNativeDateAdapter()],
  templateUrl: './reconciliation.component.html',
  styleUrl: './reconciliation.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReconciliationComponent implements OnInit {
  private readonly store = inject(Store);
  private readonly fb = inject(FormBuilder);

  readonly items = this.store.selectSignal(selectItems);
  readonly total = this.store.selectSignal(selectTotal);
  readonly loading = this.store.selectSignal(selectLoading);
  readonly error = this.store.selectSignal(selectError);
  readonly importing = this.store.selectSignal(selectImporting);
  readonly importError = this.store.selectSignal(selectImportError);

  readonly selectedFile = signal<File | null>(null);

  readonly statuses = ['Pending', 'Resolved', 'Ignored'];
  readonly displayedColumns = [
    'customerName',
    'accountNumber',
    'productName',
    'internalQuantity',
    'custodianQuantity',
    'difference',
    'status',
    'createdAt',
    'actions',
  ];

  readonly importForm = this.fb.nonNullable.group({
    source: ['', Validators.required],
    referenceDate: [null as Date | null, Validators.required],
  });

  statusFilter = '';
  pageIndex = 0;
  pageSize = 20;
  sortActive = 'createdAt';
  sortDirection: 'asc' | 'desc' = 'desc';

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    const query: DivergencesQuery = {
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
      sort: `${this.sortActive}:${this.sortDirection}`,
      status: this.statusFilter || undefined,
    };
    this.store.dispatch(ReconciliationActions.load({ query }));
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

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile.set(input.files?.[0] ?? null);
  }

  submitImport(): void {
    const file = this.selectedFile();
    if (this.importForm.invalid || !file) {
      this.importForm.markAllAsTouched();
      return;
    }
    const { source, referenceDate } = this.importForm.getRawValue();
    const formattedDate = formatDate(referenceDate!, 'yyyy-MM-dd', 'en-US');
    this.store.dispatch(ReconciliationActions.import({ source: source.trim(), referenceDate: formattedDate, file }));
  }

  resolve(id: string): void {
    this.store.dispatch(ReconciliationActions.resolve({ id }));
  }

  ignore(id: string): void {
    this.store.dispatch(ReconciliationActions.ignore({ id }));
  }
}
