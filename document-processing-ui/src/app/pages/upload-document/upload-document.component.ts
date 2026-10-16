import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { interval, of } from 'rxjs';
import { catchError, switchMap, take, takeWhile } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { DocumentService } from '../../core/services/document.service';
import { NotificationService } from '../../core/services/notification.service';
import { ProblemDetails } from '../../core/models/api.models';

@Component({
  selector: 'app-upload-document',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="page-head">
      <div>
        <h2>Upload document</h2>
        <p>Accepted types are PAN, AADHAR, and LOA. The file text must contain a matching identifier. Maximum size {{ maxMb }} MB.</p>
      </div>
    </div>
    <form class="panel form-grid" [formGroup]="form" (ngSubmit)="submit()">
      <label class="field">
        <span>Customer ID</span>
        <input formControlName="customerId" maxlength="64" autocomplete="off" />
        @if (showError('customerId')) { <small class="error-text">Customer ID is required.</small> }
      </label>
      <label class="field">
        <span>Document type</span>
        <select formControlName="documentType">
          <option value="">Select a type</option>
          <option value="PAN">PAN</option>
          <option value="AADHAR">AADHAR</option>
          <option value="LOA">LOA</option>
        </select>
        @if (showError('documentType')) { <small class="error-text">Choose PAN, AADHAR, or LOA.</small> }
      </label>
      <label class="field">
        <span>File</span>
        <input type="file" (change)="onFile($event)" />
        @if (fileName()) {
          <small class="help">{{ fileName() }} · {{ fileSize() }} bytes</small>
        }
        @if (fileError()) { <small class="error-text">{{ fileError() }}</small> }
      </label>
      @if (submitting()) {
        <p class="help">Uploading and accepting the document...</p>
      }
      <div class="actions">
        <button class="btn" type="submit" [disabled]="submitting()">Upload</button>
      </div>
      @if (documentId() && outcome() === 'working') {
        <div class="notice">Checking the submitted file. <a [routerLink]="['/documents', documentId()]">Open details</a></div>
      }
      @if (documentId() && outcome() === 'approved') {
        <div class="notice ok">Approved. The file text matched the {{ form.controls.documentType.value }} rules. <a [routerLink]="['/documents', documentId()]">Open details</a></div>
      }
      @if (resultMessage()) {
        <div class="error-panel">
          <strong>Validation failed</strong>
          <p>{{ resultMessage() }}</p>
          <a [routerLink]="['/documents', documentId()]">Open details</a>
        </div>
      }
    </form>
  `
})
export class UploadDocumentComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly documents = inject(DocumentService);
  private readonly notifications = inject(NotificationService);
  readonly maxMb = Math.round(environment.maxUploadBytes / (1024 * 1024));
  readonly submitting = signal(false);
  readonly fileName = signal('');
  readonly fileSize = signal(0);
  readonly fileError = signal('');
  readonly documentId = signal('');
  readonly resultMessage = signal('');
  readonly outcome = signal<'idle' | 'working' | 'approved' | 'rejected'>('idle');
  private readonly destroyRef = inject(DestroyRef);
  private fileContentBase64 = '';
  private submitted = false;

  readonly form = this.formBuilder.nonNullable.group({
    customerId: ['', [Validators.required, Validators.maxLength(64)]],
    documentType: ['', Validators.required]
  });

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    this.fileError.set('');
    this.fileContentBase64 = '';
    this.fileName.set('');
    this.fileSize.set(0);
    if (!file) {
      this.fileError.set('Choose a file.');
      return;
    }
    if (file.size > environment.maxUploadBytes) {
      this.fileError.set(`File exceeds the ${environment.maxUploadBytes} byte limit.`);
      return;
    }
    if (file.size === 0) {
      this.fileError.set('The file is empty.');
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      const result = String(reader.result ?? '');
      const comma = result.indexOf(',');
      this.fileContentBase64 = comma >= 0 ? result.slice(comma + 1) : result;
      this.fileName.set(file.name);
      this.fileSize.set(file.size);
    };
    reader.onerror = () => this.fileError.set('The file could not be read.');
    reader.readAsDataURL(file);
  }

  showError(name: 'customerId' | 'documentType'): boolean {
    const control = this.form.controls[name];
    return (this.submitted || control.touched) && control.invalid;
  }

  submit(): void {
    this.submitted = true;
    this.form.markAllAsTouched();
    if (!this.fileName() || !this.fileContentBase64) {
      this.fileError.set(this.fileError() || 'Choose a file.');
    }
    if (this.form.invalid || this.fileError() || !this.fileContentBase64) {
      return;
    }

    this.submitting.set(true);
    this.resultMessage.set('');
    this.outcome.set('idle');
    this.documents.upload({
      customerId: this.form.controls.customerId.value.trim(),
      documentType: this.form.controls.documentType.value,
      fileName: this.fileName(),
      fileContentBase64: this.fileContentBase64
    }).subscribe({
      next: (response) => {
        this.documentId.set(response.documentId);
        this.submitting.set(false);
        this.outcome.set('working');
        this.notifications.success('Upload accepted. Validation is running.');
        this.watchResult(response.documentId);
      },
      error: (error: HttpErrorResponse) => {
        this.submitting.set(false);
        const problem = error.error as ProblemDetails | undefined;
        const message = problem?.detail || 'Upload failed.';
        this.fileError.set(message);
        this.notifications.error(message);
      }
    });
  }

  private watchResult(documentId: string): void {
    interval(1000).pipe(
      take(20),
      switchMap(() => this.documents.getDocument(documentId).pipe(catchError(() => of(null)))),
      takeWhile((item) => !!item && item.currentStatus !== 'APPROVED' && item.currentStatus !== 'REJECTED' && item.currentStatus !== 'OCR_FAILED', true),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe((item) => {
      if (!item) {
        return;
      }
      if (item.currentStatus === 'APPROVED') {
        this.outcome.set('approved');
        return;
      }
      if (item.currentStatus !== 'REJECTED' && item.currentStatus !== 'OCR_FAILED') {
        return;
      }
      const message = item.compliance?.remarks || 'The submitted file did not pass validation.';
      this.outcome.set('rejected');
      this.resultMessage.set(message);
      this.notifications.error(message);
    });
  }
}
