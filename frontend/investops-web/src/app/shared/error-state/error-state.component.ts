import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-error-state',
  standalone: true,
  imports: [MatButtonModule, MatIconModule],
  template: `
    <div class="error">
      <mat-icon color="warn">error_outline</mat-icon>
      <p>{{ message() }}</p>
      <button mat-stroked-button color="primary" (click)="retry.emit()">Tentar novamente</button>
    </div>
  `,
  styleUrl: './error-state.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ErrorStateComponent {
  readonly message = input('Algo deu errado.');
  readonly retry = output<void>();
}
