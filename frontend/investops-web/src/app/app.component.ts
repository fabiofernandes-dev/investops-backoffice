import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
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
    MatButtonModule,
    MatProgressBarModule,
  ],
  template: `
    <mat-toolbar class="toolbar">
      <button mat-icon-button (click)="drawer.toggle()" aria-label="Alternar menu">
        <mat-icon>menu</mat-icon>
      </button>
      <mat-icon class="brand-logo">candlestick_chart</mat-icon>
      <span class="brand">InvestOps <span class="brand-sub">Backoffice</span></span>
      <span class="crumb"><mat-icon>chevron_right</mat-icon>{{ section() }}</span>
      <span class="toolbar-spacer"></span>
      <span class="env-badge" title="Instância local"><span class="dot"></span>Ambiente local</span>
    </mat-toolbar>

    <mat-sidenav-container class="layout">
      <mat-sidenav #drawer mode="side" opened class="sidenav">
        <mat-nav-list>
          <h3 matSubheader class="nav-subheader">Principal</h3>
          <a mat-list-item routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
            <mat-icon matListItemIcon>dashboard</mat-icon>
            <span matListItemTitle>Dashboard</span>
          </a>
          <a mat-list-item routerLink="/operations" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
            <mat-icon matListItemIcon>receipt_long</mat-icon>
            <span matListItemTitle>Operações</span>
          </a>
          <a mat-list-item routerLink="/operations/new" routerLinkActive="active">
            <mat-icon matListItemIcon>add_circle</mat-icon>
            <span matListItemTitle>Importar</span>
          </a>
          <a mat-list-item routerLink="/positions" routerLinkActive="active">
            <mat-icon matListItemIcon>account_balance_wallet</mat-icon>
            <span matListItemTitle>Posições</span>
          </a>

          <h3 matSubheader class="nav-subheader">Controle</h3>
          <a mat-list-item routerLink="/reconciliation" routerLinkActive="active">
            <mat-icon matListItemIcon>rule</mat-icon>
            <span matListItemTitle>Conciliação</span>
          </a>
          <a mat-list-item routerLink="/processing-monitor" routerLinkActive="active">
            <mat-icon matListItemIcon>monitor_heart</mat-icon>
            <span matListItemTitle>Processing Monitor</span>
          </a>

          <h3 matSubheader class="nav-subheader">Mercado &amp; cadastro</h3>
          <a mat-list-item routerLink="/quotes" routerLinkActive="active">
            <mat-icon matListItemIcon>trending_up</mat-icon>
            <span matListItemTitle>Cotações</span>
          </a>
          <a mat-list-item routerLink="/products" routerLinkActive="active">
            <mat-icon matListItemIcon>inventory_2</mat-icon>
            <span matListItemTitle>Produtos</span>
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
  styleUrl: './app.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent {
  readonly loading = inject(LoadingService);
  private readonly router = inject(Router);

  // Nome da seção atual (breadcrumb na toolbar), derivado da rota.
  readonly section = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.sectionFor(this.router.url)),
      startWith(this.sectionFor(this.router.url)),
    ),
    { initialValue: this.sectionFor(this.router.url) },
  );

  private sectionFor(url: string): string {
    if (url.startsWith('/operations/new')) return 'Importar operação';
    if (url.startsWith('/operations')) return 'Operações';
    if (url.startsWith('/positions')) return 'Posições';
    if (url.startsWith('/reconciliation')) return 'Conciliação';
    if (url.startsWith('/processing-monitor')) return 'Processing Monitor';
    if (url.startsWith('/quotes')) return 'Cotações';
    if (url.startsWith('/products')) return 'Produtos';
    return 'Dashboard';
  }
}
