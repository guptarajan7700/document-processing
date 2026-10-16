import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NavbarComponent } from './layout/navbar/navbar.component';
import { SidebarComponent } from './layout/sidebar/sidebar.component';
import { NotificationListComponent } from './shared/notification-list/notification-list.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, NavbarComponent, SidebarComponent, NotificationListComponent],
  template: `
    <div class="shell">
      <app-sidebar></app-sidebar>
      <div class="workspace">
        <app-navbar></app-navbar>
        <main class="page">
          <router-outlet></router-outlet>
        </main>
      </div>
    </div>
    <app-notification-list></app-notification-list>
  `
})
export class AppComponent {}
