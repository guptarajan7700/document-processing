import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <aside class="sidebar">
      <p class="brand">Orchestrator<span>PAN, AADHAR, LOA</span></p>
      <a class="nav-link" routerLink="/dashboard" routerLinkActive="active">Dashboard</a>
      <a class="nav-link" routerLink="/upload" routerLinkActive="active">Upload</a>
      <a class="nav-link" routerLink="/documents" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Documents</a>
      <a class="nav-link" routerLink="/compliance-rules" routerLinkActive="active">Compliance rules</a>
    </aside>
  `
})
export class SidebarComponent {}
