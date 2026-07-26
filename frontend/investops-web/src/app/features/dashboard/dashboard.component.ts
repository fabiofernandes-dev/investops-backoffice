import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { timer } from 'rxjs';
import { ErrorStateComponent } from '../../shared/error-state/error-state.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { DashboardActions } from './state/dashboard.actions';
import { selectError, selectLoading, selectMetrics, selectSummary } from './state/dashboard.feature';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatCardModule,
    MatProgressBarModule,
    MatButtonModule,
    MatIconModule,
    MatSlideToggleModule,
    PageHeaderComponent,
    ErrorStateComponent,
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardComponent implements OnInit {
  private readonly store = inject(Store);

  readonly summary = this.store.selectSignal(selectSummary);
  readonly metrics = this.store.selectSignal(selectMetrics);
  readonly loading = this.store.selectSignal(selectLoading);
  readonly error = this.store.selectSignal(selectError);

  readonly autoRefresh = signal(localStorage.getItem('dash.autoRefresh') === 'true');

  constructor() {
    effect(() => localStorage.setItem('dash.autoRefresh', String(this.autoRefresh())));

    timer(10000, 10000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        if (this.autoRefresh() && !document.hidden) {
          this.reload();
        }
      });
  }

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.store.dispatch(DashboardActions.load());
  }
}
