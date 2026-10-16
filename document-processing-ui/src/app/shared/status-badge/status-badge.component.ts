import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-status-badge',
  standalone: true,
  template: `<span class="badge" [class]="cssClass">{{ label }}</span>`
})
export class StatusBadgeComponent {
  @Input({ required: true }) status = '';

  get label(): string {
    return this.status.replaceAll('_', ' ');
  }

  get cssClass(): string {
    const status = this.status;
    if (status === 'APPROVED' || status === 'OCR_Success') {
      return 'badge success';
    }
    if (status === 'OCR_FAILED' || status === 'REJECTED') {
      return 'badge failed';
    }
    if (status === 'OCR_IN_PROGRESS') {
      return 'badge progress';
    }
    if (status === 'COMPLIANCE_CHECK_IN_PROGRESS') {
      return 'badge compliance';
    }
    return 'badge received';
  }
}
