import { Component, OnInit, inject, signal } from '@angular/core';
import { ComplianceRule } from '../../core/models/api.models';
import { ComplianceRulesService } from '../../core/services/compliance-rules.service';
import { LoadingIndicatorComponent } from '../../shared/loading-indicator/loading-indicator.component';

@Component({
  selector: 'app-compliance-rules',
  standalone: true,
  imports: [LoadingIndicatorComponent],
  template: `
    <div class="page-head">
      <div>
        <h2>Compliance rules</h2>
        <p>Active sample rules loaded from SQL Server. Length checks do not prove a document is genuine. Editing is not available.</p>
      </div>
      <button class="btn secondary" type="button" (click)="load()">Refresh</button>
    </div>
    <section class="panel">
      @if (loading()) {
        <app-loading-indicator message="Loading rules"></app-loading-indicator>
      } @else if (error()) {
        <div class="error-panel">
          <p>{{ error() }}</p>
          <button class="btn" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (rules().length === 0) {
        <p class="empty">No compliance rules are configured.</p>
      } @else {
        <div class="table-wrap">
          <table>
            <thead>
              <tr><th>Type</th><th>Rule</th><th>Kind</th><th>Expected length</th><th>Active</th><th>Description</th></tr>
            </thead>
            <tbody>
              @for (rule of rules(); track rule.ruleId) {
                <tr>
                  <td>{{ rule.documentType }}</td>
                  <td>{{ rule.ruleName }}</td>
                  <td>{{ rule.ruleKind }}</td>
                  <td>{{ rule.expectedLength ?? '—' }}</td>
                  <td>{{ rule.isActive ? 'Yes' : 'No' }}</td>
                  <td>{{ rule.description }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `
})
export class ComplianceRulesComponent implements OnInit {
  private readonly service = inject(ComplianceRulesService);
  readonly rules = signal<ComplianceRule[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.service.getRules().subscribe({
      next: (rules) => {
        this.rules.set(rules);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Compliance rules could not be loaded.');
        this.loading.set(false);
      }
    });
  }
}
