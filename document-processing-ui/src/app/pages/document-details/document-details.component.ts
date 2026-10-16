import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { EMPTY, interval } from 'rxjs';
import { catchError, filter, switchMap } from 'rxjs/operators';
import { DocumentDetails, ProblemDetails } from '../../core/models/api.models';
import { DocumentService } from '../../core/services/document.service';
import { NotificationService } from '../../core/services/notification.service';
import { WorkflowService } from '../../core/services/workflow.service';
import { LoadingIndicatorComponent } from '../../shared/loading-indicator/loading-indicator.component';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';

@Component({
  selector: 'app-document-details',
  standalone: true,
  imports: [DatePipe, RouterLink, LoadingIndicatorComponent, StatusBadgeComponent],
  template: `
    @if (loading() && !document()) {
      <app-loading-indicator message="Loading document"></app-loading-indicator>
    } @else if (error()) {
      <div class="panel error-panel">
        <p>{{ error() }}</p>
        <a routerLink="/documents">Back to documents</a>
      </div>
    } @else if (document()) {
      <div class="page-head">
        <div>
          <h2>Document details</h2>
          <p>{{ document()!.documentId }}</p>
        </div>
        <div class="actions">
          <button class="btn secondary" type="button" (click)="load(false)">Refresh</button>
          @if (document()!.canRetry) {
            <button class="btn" type="button" (click)="retry()" [disabled]="retrying()">
              {{ document()!.currentStatus === 'OCR_FAILED' ? 'Retry' : 'Resume' }}
            </button>
          }
        </div>
      </div>
      <section class="cards">
        <article class="card"><div class="label">Customer</div><div class="value" style="font-size:1.1rem">{{ document()!.customerId }}</div></article>
        <article class="card"><div class="label">Type</div><div class="value" style="font-size:1.1rem">{{ document()!.documentType }}</div></article>
        <article class="card"><div class="label">File</div><div class="value" style="font-size:1.1rem">{{ document()!.fileName }}</div></article>
        <article class="card"><div class="label">Uploaded</div><div class="value" style="font-size:1rem">{{ document()!.uploadedOn | date:'medium' }}</div></article>
      </section>
      <section class="panel stack" style="margin-top: 16px;">
        <div>Current status <app-status-badge [status]="document()!.currentStatus"></app-status-badge></div>
        @if (document()!.currentStatus === 'REJECTED' || document()!.currentStatus === 'OCR_FAILED') {
          <div class="error-panel">
            <strong>Validation failed</strong>
            <p>{{ document()!.compliance?.remarks || 'The submitted file did not pass validation.' }}</p>
          </div>
        }
        <a [routerLink]="['/documents', document()!.documentId, 'history']">Open workflow history</a>
        @if (document()!.ocr; as ocr) {
          <div class="notice">{{ ocr.notice }} Provider: {{ ocr.provider }}. Attempt {{ ocr.attemptNumber }}.</div>
          <div><strong>Extracted identifier:</strong> {{ ocr.extractedIdentifier || 'Not found' }}</div>
          <pre class="extract">{{ ocr.extractedText }}</pre>
        } @else {
          <p class="help">OCR text is not available yet.</p>
        }
        @if (document()!.compliance; as compliance) {
          <div>
            <strong>Compliance:</strong> {{ compliance.decision }}
            @if (document()!.currentStatus !== 'REJECTED') {
              <p>{{ compliance.remarks }}</p>
            }
          </div>
        }
      </section>
    }
  `
})
export class DocumentDetailsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly documents = inject(DocumentService);
  private readonly workflows = inject(WorkflowService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);
  readonly document = signal<DocumentDetails | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly retrying = signal(false);
  private documentId = '';

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.documentId = params.get('documentId') ?? '';
      this.document.set(null);
      this.load(true);
    });
    interval(2000).pipe(
      takeUntilDestroyed(this.destroyRef),
      filter(() => {
        const status = this.document()?.currentStatus;
        return !!this.documentId && status !== 'APPROVED' && status !== 'REJECTED' && status !== 'OCR_FAILED';
      }),
      switchMap(() => this.documents.getDocument(this.documentId).pipe(catchError(() => EMPTY)))
    ).subscribe({
      next: (item) => {
        this.document.set(item);
        if (item.currentStatus === 'APPROVED' || item.currentStatus === 'REJECTED' || item.currentStatus === 'OCR_FAILED') {
          this.loading.set(false);
        }
      }
    });
  }

  load(showSpinner: boolean): void {
    if (showSpinner) {
      this.loading.set(true);
    }
    this.documents.getDocument(this.documentId).subscribe({
      next: (item) => {
        this.document.set(item);
        this.loading.set(false);
        this.error.set('');
      },
      error: () => {
        this.error.set('This document could not be loaded.');
        this.loading.set(false);
      }
    });
  }

  retry(): void {
    this.retrying.set(true);
    this.workflows.retry(this.documentId).subscribe({
      next: (result) => {
        this.retrying.set(false);
        this.notifications.success(result.message);
        this.load(false);
      },
      error: (error: HttpErrorResponse) => {
        this.retrying.set(false);
        const problem = error.error as ProblemDetails | undefined;
        this.notifications.error(problem?.detail || 'Retry could not be started.');
      }
    });
  }
}
