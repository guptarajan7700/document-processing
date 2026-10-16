import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  DocumentDetails,
  DocumentQuery,
  DocumentSummary,
  PagedDocuments,
  UploadDocumentRequest,
  UploadDocumentResponse
} from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl;

  upload(request: UploadDocumentRequest): Observable<UploadDocumentResponse> {
    return this.http.post<UploadDocumentResponse>(`${this.baseUrl}/api/documents/upload`, request);
  }

  getSummary(): Observable<DocumentSummary> {
    return this.http.get<DocumentSummary>(`${this.baseUrl}/api/documents/summary`);
  }

  getDocuments(query: DocumentQuery): Observable<PagedDocuments> {
    let params = new HttpParams()
      .set('page', query.page)
      .set('pageSize', query.pageSize);
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.documentType) {
      params = params.set('documentType', query.documentType);
    }
    if (query.search) {
      params = params.set('search', query.search);
    }
    return this.http.get<PagedDocuments>(`${this.baseUrl}/api/documents`, { params });
  }

  getDocument(documentId: string): Observable<DocumentDetails> {
    return this.http.get<DocumentDetails>(`${this.baseUrl}/api/documents/${documentId}`);
  }
}
