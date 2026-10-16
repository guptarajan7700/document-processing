import { Injectable, signal } from '@angular/core';

export interface AppNotification {
  id: number;
  kind: 'success' | 'error' | 'info';
  text: string;
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly items = signal<AppNotification[]>([]);
  readonly notifications = this.items.asReadonly();
  private nextId = 1;

  success(text: string): void {
    this.push('success', text);
  }

  error(text: string): void {
    this.push('error', text);
  }

  dismiss(id: number): void {
    this.items.update((list) => list.filter((item) => item.id !== id));
  }

  private push(kind: AppNotification['kind'], text: string): void {
    const id = this.nextId++;
    this.items.update((list) => [...list, { id, kind, text }]);
  }
}
