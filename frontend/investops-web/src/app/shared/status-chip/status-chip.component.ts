import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-status-chip',
  standalone: true,
  template: `<span class="chip" [style.background]="color()">{{ status() }}</span>`,
  styleUrl: './status-chip.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusChipComponent {
  readonly status = input.required<string>();

  readonly color = computed(() => {
    switch (this.status()) {
      case 'Processed':
        return '#2e7d32';
      case 'Rejected':
        return '#c62828';
      case 'Failed':
        return '#ef6c00';
      case 'Validated':
        return '#1565c0';
      default:
        return '#616161';
    }
  });
}
