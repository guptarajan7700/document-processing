import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DocumentService } from '../../core/services/document.service';
import { DocumentItem } from '../../core/models/api.models';
import { LoadingIndicatorComponent } from '../../shared/loading-indicator/loading-indicator.component';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';

@Component({
  selector: 'app-documents-list',
  standalone: true,
  imports: [DatePipe, FormsModule, RouterLink, LoadingIndicatorComponent, StatusBadgeComponent],
  template: `
    <div class="page-head">
      <div>
        <h2>Documents</h2>
        <p>Search, filter, and open a workflow.</p>
      </div>
      <button class="btn secondary" type="button" (click)="load()">Refresh</button>
    </div>
    <form class="filters panel" (ngSubmit)="apply()">
      <label class="field"><span>Search</span><input [(ngModel)]="search" name="search" placeholder="Customer or file name" /></label>
      <label class="field"><span>Status</span>
        <select [(ngModel)]="status" name="status">
          <option value="">All</option>
          <option value="RECEIVED">Received</option>
          <option value="OCR_IN_PROGRESS">OCR in progress</option>
          <option value="OCR_FAILED">OCR failed</option>
          <option value="OCR_Success">OCR success</option>
          <option value="COMPLIANCE_CHECK_IN_PROGRESS">Compliance in progress</option>
          <option value="APPROVED">Approved</option>
          <option value="REJECTED">Rejected</option>
        </select>
      </label>
      <label class="field"><span>Type</span>
        <select [(ngModel)]="documentType" name="documentType">
          <option value="">All</option>
          <option>PAN</option>
          <option>AADHAR</option>
          <option>LOA</option>
        </select>
      </label>
      <button class="btn" type="submit">Apply</button>
    </form>

    <section class="panel" style="margin-top: 16px;">
      @if (loading()) {
        <app-loading-indicator message="Loading documents"></app-loading-indicator>
      } @else if (error()) {
        <div class="error-panel">
          <p>{{ error() }}</p>
          <button class="btn" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (items().length === 0) {
        <p class="empty">No documents match these filters.</p>
      } @else {
        <div class="table-wrap">
          <table class="doc-table">
            <thead>
              <tr><th>File</th><th>Customer</th><th>Type</th><th>Uploaded</th><th>Status</th><th>Latest result</th><th></th></tr>
            </thead>
            <tbody>
              @for (item of items(); track item.documentId) {
                <tr>
                  <td data-label="File">
                    <div>{{ item.fileName }}</div>
                    <div class="mono help" [title]="item.documentId">{{ item.documentId.slice(0, 8) }}</div>
                  </td>
                  <td data-label="Customer">{{ item.customerId }}</td>
                  <td data-label="Type">{{ item.documentType }}</td>
                  <td data-label="Uploaded">{{ item.uploadedOn | date:'medium' }}</td>
                  <td data-label="Status"><app-status-badge [status]="item.currentStatus"></app-status-badge></td>
                  <td data-label="Latest result" class="clip"><div class="clip-text" [title]="item.latestMessage || ''">{{ item.latestMessage || '—' }}</div></td>
                  <td data-label=""><a [routerLink]="['/documents', item.documentId]">View</a></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="pager">
          <span class="help">{{ total() }} documents</span>
          <div class="actions">
            <button class="btn secondary" type="button" (click)="previous()" [disabled]="page === 1">Previous</button>
            <span>Page {{ page }}</span>
            <button class="btn secondary" type="button" (click)="next()" [disabled]="page * pageSize >= total()">Next</button>
          </div>
        </div>
      }
    </section>
  `
})
export class DocumentsListComponent implements OnInit {
  private readonly documents = inject(DocumentService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly items = signal<DocumentItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);
  readonly error = signal('');
  search = '';
  status = '';
  documentType = '';
  page = 1;
  readonly pageSize = 10;

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.status = params.get('status') ?? '';
      this.documentType = params.get('documentType') ?? '';
      this.search = params.get('search') ?? '';
      this.page = Number(params.get('page') ?? 1) || 1;
      this.load();
    });
  }

  apply(): void {
    this.page = 1;
    this.updateQuery();
  }

  previous(): void {
    if (this.page > 1) {
      this.page -= 1;
      this.updateQuery();
    }
  }

  next(): void {
    if (this.page * this.pageSize < this.total()) {
      this.page += 1;
      this.updateQuery();
    }
  }

  private updateQuery(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        status: this.status || null,
        documentType: this.documentType || null,
        search: this.search.trim() || null,
        page: this.page > 1 ? this.page : null
      }
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.documents.getDocuments({
      page: this.page,
      pageSize: this.pageSize,
      search: this.search.trim() || undefined,
      status: this.status || undefined,
      documentType: this.documentType || undefined
    }).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('The document list could not be loaded.');
        this.loading.set(false);
      }
    });
  }
}
