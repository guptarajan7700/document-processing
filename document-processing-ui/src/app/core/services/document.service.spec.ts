import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { DocumentService } from './document.service';

describe('DocumentService', () => {
  let service: DocumentService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(DocumentService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends status and type filters to the documents endpoint', () => {
    service.getDocuments({ page: 2, pageSize: 10, status: 'APPROVED', documentType: 'PAN', search: 'CUST' }).subscribe();
    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/documents'));
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('status')).toBe('APPROVED');
    expect(request.request.params.get('documentType')).toBe('PAN');
    expect(request.request.params.get('search')).toBe('CUST');
    request.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
  });
});
