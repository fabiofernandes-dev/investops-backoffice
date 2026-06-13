import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Store } from '@ngrx/store';
import { DashboardActions } from './state/dashboard.actions';
import { selectError, selectLoading, selectMetrics, selectSummary } from './state/dashboard.feature';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatProgressBarModule, MatButtonModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  private readonly store = inject(Store);

  readonly summary = this.store.selectSignal(selectSummary);
  readonly metrics = this.store.selectSignal(selectMetrics);
  readonly loading = this.store.selectSignal(selectLoading);
  readonly error = this.store.selectSignal(selectError);

  ngOnInit(): void {
    this.store.dispatch(DashboardActions.load());
  }

  reload(): void {
    this.store.dispatch(DashboardActions.load());
  }
}
