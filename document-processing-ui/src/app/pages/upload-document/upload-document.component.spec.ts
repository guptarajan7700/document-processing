import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { UploadDocumentComponent } from './upload-document.component';

describe('UploadDocumentComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UploadDocumentComponent],
      providers: [provideHttpClient(), provideRouter([])]
    }).compileComponents();
  });

  it('requires customer id, document type, and a file', () => {
    const fixture = TestBed.createComponent(UploadDocumentComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    expect(component.form.invalid).toBeTrue();
    component.submit();
    expect(component.fileError()).toContain('Choose a file');
    component.form.controls.customerId.setValue('CUST-1');
    component.form.controls.documentType.setValue('PAN');
    expect(component.form.valid).toBeTrue();
  });
});
