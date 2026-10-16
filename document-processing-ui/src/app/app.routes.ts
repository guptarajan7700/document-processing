import { Routes } from '@angular/router';
import { ComplianceRulesComponent } from './pages/compliance-rules/compliance-rules.component';
import { DashboardComponent } from './pages/dashboard/dashboard.component';
import { DocumentDetailsComponent } from './pages/document-details/document-details.component';
import { DocumentsListComponent } from './pages/documents-list/documents-list.component';
import { UploadDocumentComponent } from './pages/upload-document/upload-document.component';
import { WorkflowHistoryComponent } from './pages/workflow-history/workflow-history.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: 'dashboard', component: DashboardComponent },
  { path: 'upload', component: UploadDocumentComponent },
  { path: 'documents', component: DocumentsListComponent },
  { path: 'documents/:documentId', component: DocumentDetailsComponent },
  { path: 'documents/:documentId/history', component: WorkflowHistoryComponent },
  { path: 'compliance-rules', component: ComplianceRulesComponent },
  { path: '**', redirectTo: 'dashboard' }
];
