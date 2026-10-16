import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DocumentStatus, WorkflowAction, WorkflowHistoryItem } from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class WorkflowService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl;

  getStatus(documentId: string): Observable<DocumentStatus> {
    return this.http.get<DocumentStatus>(`${this.baseUrl}/api/documents/${documentId}/status`);
  }

  getHistory(documentId: string): Observable<WorkflowHistoryItem[]> {
    return this.http.get<WorkflowHistoryItem[]>(`${this.baseUrl}/api/documents/${documentId}/history`);
  }

  getRecentActivity(take = 8): Observable<WorkflowHistoryItem[]> {
    const params = new HttpParams().set('take', take);
    return this.http.get<WorkflowHistoryItem[]>(`${this.baseUrl}/api/documents/activity`, { params });
  }

  retry(documentId: string): Observable<WorkflowAction> {
    return this.http.post<WorkflowAction>(`${this.baseUrl}/api/documents/${documentId}/retry`, {});
  }
}
