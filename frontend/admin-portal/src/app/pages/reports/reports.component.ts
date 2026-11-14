import { localDate } from '../../core/dates';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ClassRoom, ReportType } from '../../core/models';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Reports</div>
          <h1>Report centre</h1>
          <p class="subtitle">Generate CSV extracts for finance, academics and operations. Every export is audited.</p>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      <section class="card">
        <div class="card-header"><h3>Export a report</h3></div>
        <div class="card-body">
          <form [formGroup]="form" class="filters" style="grid-template-columns:1fr 1fr 1fr 1fr auto" (ngSubmit)="export()">
            <label class="field">
              <span>Report type</span>
              <select formControlName="type">
                @for (type of types(); track type.key) { <option [value]="type.key">{{ type.name }}</option> }
              </select>
            </label>
            <label class="field"><span>From</span><input type="date" formControlName="from" /></label>
            <label class="field"><span>To</span><input type="date" formControlName="to" /></label>
            <label class="field">
              <span>Class</span>
              <select formControlName="classId">
                <option value="">All classes</option>
                @for (item of classes(); track item.id) { <option [value]="item.id">{{ item.name }}</option> }
              </select>
            </label>
            <div class="row" style="align-items:flex-end">
              <button class="btn btn-primary" type="submit" [disabled]="loading()">
                {{ loading() ? 'Preparing…' : 'Download CSV' }}
              </button>
            </div>
          </form>
        </div>
      </section>

      <section class="stat-grid">
        @for (type of types(); track type.key) {
          <article class="card">
            <div class="card-body">
              <div class="small muted" style="text-transform:uppercase;letter-spacing:.08em;font-weight:700">Report</div>
              <h3 style="margin-top:6px">{{ type.name }}</h3>
              <p class="small muted" style="margin-top:6px;min-height:36px">{{ type.description }}</p>
              <button class="btn btn-ghost btn-sm" type="button" (click)="quickExport(type.key)">Export this report</button>
            </div>
          </article>
        }
      </section>

      <section class="card">
        <div class="card-header"><h3>What is included</h3></div>
        <div class="card-body small muted">
          <p>Student and employee reports include contact details and status. Fee reports include receipt numbers,
          payment modes and collected amounts. Attendance reports include date, class, section and status.
          Reports are scoped to your school and branch permissions.</p>
        </div>
      </section>
    </div>
  `
})
export class ReportsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  readonly auth = inject(AuthService);

  readonly types = signal<ReportType[]>([]);
  readonly classes = signal<ClassRoom[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    type: ['students'],
    from: [firstOfMonth()],
    to: [iso(new Date())],
    classId: ['']
  });

  ngOnInit(): void {
    this.api.get<ReportType[]>('reports/types').subscribe({
      next: types => this.types.set(types),
      error: () => this.error.set('Could not load report types.')
    });
    this.api.get<ClassRoom[]>('admin/classes').subscribe({
      next: classes => this.classes.set(classes),
      error: () => this.error.set('Some information could not be loaded. Check your connection and refresh the page.')
    });
  }

  quickExport(type: string): void {
    this.form.patchValue({ type });
    this.export();
  }

  export(): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.error.set(null);

    const value = this.form.getRawValue();
    this.api
      .download('reports/export', {
        type: value.type,
        from: value.from,
        to: value.to,
        classId: value.classId
      })
      .subscribe({
        next: response => {
          this.api.saveBlob(response, `${value.type}-${value.from}-to-${value.to}.csv`);
          this.loading.set(false);
          this.message.set('Report downloaded.');
        },
        error: () => {
          this.loading.set(false);
          this.error.set('The report could not be generated.');
        }
      });
  }
}

function iso(date: Date): string {
  return localDate(date);
}

function firstOfMonth(): string {
  const now = new Date();
  return iso(new Date(now.getFullYear(), now.getMonth(), 1));
}
