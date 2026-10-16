import { Component } from '@angular/core';

@Component({
  selector: 'app-navbar',
  standalone: true,
  template: `
    <header class="topbar">
      <h1>Document Processing</h1>
      <span class="help">Local compliance workflow</span>
    </header>
  `
})
export class NavbarComponent {}
