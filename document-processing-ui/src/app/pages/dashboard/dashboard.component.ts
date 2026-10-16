import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DocumentService } from '../../core/services/document.service';
import { WorkflowService } from '../../core/services/workflow.service';
import { DocumentSummary, WorkflowHistoryItem } from '../../core/models/api.models';
import { LoadingIndicatorComponent } from '../../shared/loading-indicator/loading-indicator.component';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, DatePipe, LoadingIndicatorComponent, StatusBadgeComponent],
  template: `
    <div class="page-head">
      <div>
        <h2>Dashboard</h2>
        <p>Counts and recent activity come from the processing API.</p>
      </div>
      <button class="btn secondary" type="button" (click)="load()">Refresh</button>
    </div>

    @if (loading()) {
      <app-loading-indicator message="Loading dashboard"></app-loading-indicator>
    } @else if (error()) {
      <div class="panel error-panel">
        <p>{{ error() }}</p>
        <button class="btn" type="button" (click)="load()">Try again</button>
      </div>
    } @else if (summary()) {
      <section class="cards">
        <a class="card" routerLink="/documents"><div class="label">Total documents</div><div class="value">{{ summary()!.total }}</div></a>
        <a class="card" routerLink="/documents" [queryParams]="{ status: 'RECEIVED' }"><div class="label">Received</div><div class="value">{{ summary()!.received }}</div></a>
        <a class="card" routerLink="/documents" [queryParams]="{ status: 'OCR_IN_PROGRESS' }"><div class="label">OCR in progress</div><div class="value">{{ summary()!.ocrInProgress }}</div></a>
        <a class="card" routerLink="/documents" [queryParams]="{ status: 'OCR_FAILED' }"><div class="label">OCR failures</div><div class="value">{{ summary()!.ocrFailed }}</div></a>
        <a class="card" routerLink="/documents" [queryParams]="{ status: 'OCR_Success' }"><div class="label">OCR success</div><div class="value">{{ summary()!.ocrSuccess }}</div></a>
        <a class="card" routerLink="/documents" [queryParams]="{ status: 'COMPLIANCE_CHECK_IN_PROGRESS' }"><div class="label">Compliance in progress</div><div class="value">{{ summary()!.complianceInProgress }}</div></a>
        <a class="card" routerLink="/documents" [queryParams]="{ status: 'APPROVED' }"><div class="label">Approved</div><div class="value">{{ summary()!.approved }}</div></a>
        <a class="card" routerLink="/documents" [queryParams]="{ status: 'REJECTED' }"><div class="label">Rejected</div><div class="value">{{ summary()!.rejected }}</div></a>
      </section>

      <section class="panel" style="margin-top: 16px;">
        <h3>Recent workflow activity</h3>
        @if (activity().length === 0) {
          <p class="empty">No workflow events yet. Upload a document to start a workflow.</p>
        } @else {
          <ul class="timeline">
            @for (item of activity(); track item.id) {
              <li>
                <div class="when">{{ item.timestamp | date:'medium' }} · {{ item.agentName || 'System' }}</div>
                <div><app-status-badge [status]="item.status"></app-status-badge></div>
                <p>{{ item.message }}</p>
                <a [routerLink]="['/documents', item.documentId, 'history']">View history</a>
              </li>
            }
          </ul>
        }
      </section>
    }
  `
})
export class DashboardComponent implements OnInit {
  private readonly documents = inject(DocumentService);
  private readonly workflows = inject(WorkflowService);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly summary = signal<DocumentSummary | null>(null);
  readonly activity = signal<WorkflowHistoryItem[]>([]);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.documents.getSummary().subscribe({
      next: (summary) => {
        this.summary.set(summary);
        this.workflows.getRecentActivity(8).subscribe({
          next: (activity) => {
            this.activity.set(activity);
            this.loading.set(false);
          },
          error: () => this.fail('Recent activity could not be loaded.')
        });
      },
      error: () => this.fail('Dashboard counts could not be loaded. Check that the API is running.')
    });
  }

  private fail(message: string): void {
    this.error.set(message);
    this.loading.set(false);
  }
}
