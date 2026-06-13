import { Component, inject } from '@angular/core';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { LoadingService } from './core/state/loading.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  template: `
    <mat-toolbar color="primary">
      <span>InvestOps Backoffice</span>
    </mat-toolbar>

    <mat-sidenav-container class="layout">
      <mat-sidenav mode="side" opened class="sidenav">
        <mat-nav-list>
          <a mat-list-item routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
            <mat-icon matListItemIcon>dashboard</mat-icon>
            <span matListItemTitle>Dashboard</span>
          </a>
          <a mat-list-item routerLink="/operations" routerLinkActive="active">
            <mat-icon matListItemIcon>receipt_long</mat-icon>
            <span matListItemTitle>Operações</span>
          </a>
          <a mat-list-item routerLink="/positions" routerLinkActive="active">
            <mat-icon matListItemIcon>account_balance_wallet</mat-icon>
            <span matListItemTitle>Posições</span>
          </a>
        </mat-nav-list>
      </mat-sidenav>

      <mat-sidenav-content class="content">
        @if (loading.loading()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <div class="page">
          <router-outlet />
        </div>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: [
    `
      .layout {
        height: calc(100vh - 64px);
      }
      .sidenav {
        width: 220px;
      }
      .active {
        background: rgba(0, 0, 0, 0.06);
      }
      .page {
        padding: 24px;
      }
    `,
  ],
})
export class AppComponent {
  readonly loading = inject(LoadingService);
}
