import { TestBed } from '@angular/core/testing';
import { StatusBadgeComponent } from './status-badge.component';

describe('StatusBadgeComponent', () => {
  it('shows a readable status label', () => {
    const fixture = TestBed.createComponent(StatusBadgeComponent);
    fixture.componentInstance.status = 'OCR_Success';
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('OCR Success');
    expect(fixture.componentInstance.cssClass).toContain('success');
  });
});
