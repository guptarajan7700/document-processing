import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-loading-indicator',
  standalone: true,
  template: `<div class="loading-row" role="status"><span class="spinner"></span><span>{{ message }}</span></div>`
})
export class LoadingIndicatorComponent {
  @Input() message = 'Loading...';
}
