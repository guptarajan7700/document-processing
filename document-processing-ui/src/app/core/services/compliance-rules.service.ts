import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ComplianceRule } from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class ComplianceRulesService {
  private readonly http = inject(HttpClient);

  getRules(): Observable<ComplianceRule[]> {
    return this.http.get<ComplianceRule[]>(`${environment.apiBaseUrl}/api/compliance-rules`);
  }
}
