import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  readonly lastError = signal<string | null>(null);

  error(message: string): void {
    this.lastError.set(message);
  }

  clear(): void {
    this.lastError.set(null);
  }
}
