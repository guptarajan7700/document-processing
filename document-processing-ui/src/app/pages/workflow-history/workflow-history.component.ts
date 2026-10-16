import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { WorkflowHistoryItem } from '../../core/models/api.models';
import { WorkflowService } from '../../core/services/workflow.service';
import { LoadingIndicatorComponent } from '../../shared/loading-indicator/loading-indicator.component';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';

@Component({
  selector: 'app-workflow-history',
  standalone: true,
  imports: [DatePipe, RouterLink, LoadingIndicatorComponent, StatusBadgeComponent],
  template: `
    <div class="page-head">
      <div>
        <h2>Workflow history</h2>
        <p>{{ documentId }}</p>
      </div>
      <div class="actions">
        <a class="btn secondary" [routerLink]="['/documents', documentId]">Back to details</a>
        <button class="btn secondary" type="button" (click)="load()">Refresh</button>
      </div>
    </div>
    <section class="panel">
      @if (loading()) {
        <app-loading-indicator message="Loading history"></app-loading-indicator>
      } @else if (error()) {
        <div class="error-panel">
          <p>{{ error() }}</p>
          <button class="btn" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (items().length === 0) {
        <p class="empty">No workflow events are recorded for this document.</p>
      } @else {
        <ol class="timeline">
          @for (item of items(); track item.id) {
            <li>
              <div class="when">{{ item.timestamp | date:'medium' }}</div>
              <app-status-badge [status]="item.status"></app-status-badge>
              <p>{{ item.message }}</p>
              <p class="help">
                {{ item.agentName || 'System' }}
                @if (item.toolName) { · {{ item.toolName }} }
                @if (item.attemptNumber) { · attempt {{ item.attemptNumber }} }
                @if (item.exceptionType) { · {{ item.exceptionType }} }
                @if (item.durationMs !== null) { · {{ item.durationMs }} ms }
              </p>
            </li>
          }
        </ol>
      }
    </section>
  `
})
export class WorkflowHistoryComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly workflows = inject(WorkflowService);
  private readonly destroyRef = inject(DestroyRef);
  documentId = '';
  readonly items = signal<WorkflowHistoryItem[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.documentId = params.get('documentId') ?? '';
      this.load();
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.workflows.getHistory(this.documentId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Workflow history could not be loaded.');
        this.loading.set(false);
      }
    });
  }
}
