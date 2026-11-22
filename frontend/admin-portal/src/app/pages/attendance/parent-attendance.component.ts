import { localDate } from '../../core/dates';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { Child } from '../../core/models';

interface ParentAttendanceSummary {
  totalDays: number;
  present: number;
  absent: number;
  late: number;
  halfDay: number;
  percentage: number;
}

const DAY_MS = 86400000;

/** Read-only attendance for the children linked to the signed-in parent. */
@Component({
  selector: 'app-parent-attendance',
  standalone: true,
  imports: [FormsModule, DatePipe, DecimalPipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Attendance</div>
          <h1>My children's attendance</h1>
          <p class="subtitle">A child-scoped summary from the parent portal. Staff registers are not available here.</p>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }

      <section class="card">
        <div class="card-body filters filters-auto">
          <label class="field">
            <span>Child</span>
            <select [ngModel]="childId()" (ngModelChange)="setChild($event)">
              @for (child of children(); track child.studentId) {
                <option [ngValue]="child.studentId">{{ child.fullName }} · {{ child.className }}–{{ child.sectionName }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span>From</span>
            <input type="date" [ngModel]="from()" (ngModelChange)="from.set($event)" />
          </label>
          <label class="field">
            <span>To</span>
            <input type="date" [ngModel]="to()" (ngModelChange)="to.set($event)" />
          </label>
          <div class="row" style="align-items:flex-end">
            <button class="btn btn-primary" (click)="load()" [disabled]="!childId() || loading()">
              {{ loading() ? 'Loading…' : 'Refresh' }}
            </button>
          </div>
        </div>
      </section>

      @if (loading()) {
        <div class="loading">Loading attendance…</div>
      } @else {
        @if (summary(); as data) {
        <section class="card">
          <div class="card-header">
            <div>
              <h3>{{ selectedChild()?.fullName }}</h3>
              <span class="small muted">{{ selectedChild()?.className }} – {{ selectedChild()?.sectionName }} · {{ from() | date:'dd MMM' }} – {{ to() | date:'dd MMM yyyy' }}</span>
            </div>
            <span class="badge teal">{{ data.totalDays }} school days</span>
          </div>
          <div class="card-body">
            <div class="stat-grid">
              <div class="stat tone-positive"><div class="label">Present</div><div class="value">{{ data.present }}</div></div>
              <div class="stat tone-warning"><div class="label">Late</div><div class="value">{{ data.late }}</div></div>
              <div class="stat tone-danger"><div class="label">Absent</div><div class="value">{{ data.absent }}</div></div>
              <div class="stat tone-info"><div class="label">Attendance</div><div class="value">{{ data.percentage | number:'1.0-1' }}<span class="unit">%</span></div></div>
            </div>
            @if (data.halfDay) {
              <div class="divider"></div>
              <div class="small muted">{{ data.halfDay }} half-day marking{{ data.halfDay === 1 ? '' : 's' }} in this period.</div>
            }
          </div>
        </section>
        } @else {
          <div class="empty">Choose a child and date range to see attendance.</div>
        }
      }
    </div>
  `
})
export class ParentAttendanceComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly children = signal<Child[]>([]);
  readonly childId = signal<number | null>(null);
  readonly from = signal(daysAgo(30));
  readonly to = signal(daysAgo(0));
  readonly summary = signal<ParentAttendanceSummary | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.get<Child[]>('parent/children').subscribe({
      next: children => {
        this.children.set(children);
        if (children.length) {
          this.childId.set(children[0].studentId);
          this.load();
        }
      },
      error: () => this.error.set('Could not load your children. Refresh the page and try again.')
    });
  }

  selectedChild(): Child | undefined {
    return this.children().find(child => child.studentId === this.childId());
  }

  setChild(childId: number | null): void {
    this.childId.set(childId);
    this.summary.set(null);
    if (childId) this.load();
  }

  load(): void {
    const childId = this.childId();
    if (!childId) return;

    this.loading.set(true);
    this.error.set(null);
    this.api.get<ParentAttendanceSummary>(`parent/children/${childId}/attendance`, {
      from: this.from(),
      to: this.to()
    }).subscribe({
      next: summary => {
        this.summary.set(summary);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load attendance for this child.');
        this.summary.set(null);
        this.loading.set(false);
      }
    });
  }
}

function daysAgo(days: number): string {
  return localDate(new Date(Date.now() - days * DAY_MS));
}
