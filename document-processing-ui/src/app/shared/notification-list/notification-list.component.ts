import { Component, inject } from '@angular/core';
import { NotificationService } from '../../core/services/notification.service';

@Component({
  selector: 'app-notification-list',
  standalone: true,
  template: `
    <div class="toasts" aria-live="polite">
      @for (item of notifications(); track item.id) {
        <div class="toast" [class.error]="item.kind === 'error'">
          <span>{{ item.text }}</span>
          <button type="button" (click)="dismiss(item.id)" aria-label="Dismiss notification">Close</button>
        </div>
      }
    </div>
  `
})
export class NotificationListComponent {
  private readonly service = inject(NotificationService);
  readonly notifications = this.service.notifications;

  dismiss(id: number): void {
    this.service.dismiss(id);
  }
}
