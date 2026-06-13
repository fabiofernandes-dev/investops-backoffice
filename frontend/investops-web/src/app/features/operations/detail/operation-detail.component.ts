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
import { OperationsActions } from '../state/operations.actions';
import { selectAudit, selectDetail, selectDetailError, selectDetailLoading, selectEvents } from '../state/operations.feature';

@Component({
  selector: 'app-operation-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, MatTabsModule, MatTableModule, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  templateUrl: './operation-detail.component.html',
  styles: [
    `
      .header {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-bottom: 8px;
      }
      table {
        width: 100%;
      }
      dl {
        display: grid;
        grid-template-columns: 200px 1fr;
        gap: 8px 16px;
        max-width: 640px;
      }
      dt {
        color: rgba(0, 0, 0, 0.6);
      }
      .state {
        padding: 24px;
        text-align: center;
        color: rgba(0, 0, 0, 0.6);
      }
    `,
  ],
})
export class OperationDetailComponent implements OnInit {
  private readonly store = inject(Store);
  private readonly route = inject(ActivatedRoute);

  readonly detail = this.store.selectSignal(selectDetail);
  readonly events = this.store.selectSignal(selectEvents);
  readonly audit = this.store.selectSignal(selectAudit);
  readonly loading = this.store.selectSignal(selectDetailLoading);
  readonly error = this.store.selectSignal(selectDetailError);

  readonly eventColumns = ['eventType', 'status', 'attemptCount', 'errorMessage', 'createdAt', 'processedAt'];
  readonly auditColumns = ['action', 'userId', 'newValue', 'createdAt'];

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.store.dispatch(OperationsActions.loadDetail({ id }));
    }
  }
}
