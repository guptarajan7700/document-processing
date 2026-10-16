export interface UploadDocumentRequest {
  customerId: string;
  documentType: string;
  fileName: string;
  fileContentBase64: string;
}

export interface UploadDocumentResponse {
  documentId: string;
  status: string;
  message: string;
}

export interface DocumentItem {
  documentId: string;
  customerId: string;
  documentType: string;
  fileName: string;
  uploadedOn: string;
  currentStatus: string;
  latestMessage?: string | null;
}

export interface PagedDocuments {
  items: DocumentItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface DocumentSummary {
  total: number;
  received: number;
  ocrInProgress: number;
  ocrFailed: number;
  ocrSuccess: number;
  complianceInProgress: number;
  approved: number;
  rejected: number;
}

export interface DocumentStatus {
  documentId: string;
  currentStatus: string;
  uploadedOn: string;
  lastStatusOn: string | null;
}

export interface OcrDetails {
  provider: string;
  isMock: boolean;
  extractedIdentifier: string | null;
  extractedText: string;
  attemptNumber: number;
  createdOn: string;
  notice: string;
}

export interface ComplianceDetails {
  decision: string | null;
  remarks: string | null;
}

export interface DocumentDetails extends DocumentItem {
  canRetry: boolean;
  ocr: OcrDetails | null;
  compliance: ComplianceDetails | null;
}

export interface WorkflowHistoryItem {
  id: number;
  documentId: string;
  status: string;
  message: string;
  timestamp: string;
  agentName: string | null;
  toolName: string | null;
  attemptNumber: number | null;
  correlationId: string | null;
  durationMs: number | null;
  exceptionType: string | null;
}

export interface ComplianceRule {
  ruleId: number;
  documentType: string;
  ruleName: string;
  ruleKind: string;
  expectedLength: number | null;
  isActive: boolean;
  description: string | null;
}

export interface WorkflowAction {
  documentId: string;
  status: string;
  outcome: string;
  message: string;
}

export interface DocumentQuery {
  page: number;
  pageSize: number;
  status?: string;
  documentType?: string;
  search?: string;
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: string[];
}
