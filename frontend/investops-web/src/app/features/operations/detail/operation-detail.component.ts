import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { ErrorStateComponent } from '../../../shared/error-state/error-state.component';
import { OperationsActions } from '../state/operations.actions';
import { selectAudit, selectDetail, selectDetailError, selectDetailLoading, selectEvents } from '../state/operations.feature';

@Component({
  selector: 'app-operation-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatTabsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatTooltipModule,
    ErrorStateComponent,
    EmptyStateComponent,
  ],
  templateUrl: './operation-detail.component.html',
  styleUrl: './operation-detail.component.scss',
})
export class OperationDetailComponent implements OnInit {
  private readonly store = inject(Store);
  private readonly route = inject(ActivatedRoute);
  private operationId: string | null = null;

  readonly detail = this.store.selectSignal(selectDetail);
  readonly events = this.store.selectSignal(selectEvents);
  readonly audit = this.store.selectSignal(selectAudit);
  readonly loading = this.store.selectSignal(selectDetailLoading);
  readonly error = this.store.selectSignal(selectDetailError);

  readonly eventColumns = ['eventType', 'status', 'attemptCount', 'errorMessage', 'createdAt', 'processedAt'];
  readonly auditColumns = ['action', 'userId', 'newValue', 'createdAt'];

  ngOnInit(): void {
    this.operationId = this.route.snapshot.paramMap.get('id');
    this.reload();
  }

  reload(): void {
    if (this.operationId) {
      this.store.dispatch(OperationsActions.loadDetail({ id: this.operationId }));
    }
  }
}
