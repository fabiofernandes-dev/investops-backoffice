import { Injectable, computed, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class LoadingService {
  private readonly inFlight = signal(0);
  readonly loading = computed(() => this.inFlight() > 0);

  start(): void {
    this.inFlight.update((n) => n + 1);
  }

  stop(): void {
    this.inFlight.update((n) => Math.max(0, n - 1));
  }
}
