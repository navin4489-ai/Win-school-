import { localDate } from '../../core/dates';
import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ClassAttendance, ClassRoom, Section } from '../../core/models';

@Component({
  selector: 'app-attendance',
  standalone: true,
  imports: [FormsModule, DatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Attendance</div>
          <h1>Class attendance</h1>
          <p class="subtitle">Mark attendance for a class and section. Parents are notified for absences.</p>
        </div>
        <div class="toolbar">
          @if (attendance(); as a) {
            <span [class]="'badge ' + (a.isSubmitted ? 'green' : 'amber')">
              {{ a.isSubmitted ? 'Already submitted' : 'Not submitted' }}
            </span>
          }
          <button class="btn btn-ghost btn-sm" (click)="markAllPresent()">Mark all present</button>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      <section class="card">
        <div class="card-body">
          <div class="filters" style="grid-template-columns:1fr 1fr 1fr auto">
            <label class="field">
              <span>Class</span>
              <select [ngModel]="classId()" (ngModelChange)="setClass($event)">
                <option [ngValue]="null">Select class</option>
                @for (item of classes(); track item.id) { <option [ngValue]="item.id">{{ item.name }}</option> }
              </select>
            </label>
            <label class="field">
              <span>Section</span>
              <select [ngModel]="sectionId()" (ngModelChange)="sectionId.set($event)">
                <option [ngValue]="null">Select section</option>
                @for (item of sections(); track item.id) { <option [ngValue]="item.id">{{ item.name }}</option> }
              </select>
            </label>
            <label class="field">
              <span>Date</span>
              <input type="date" [ngModel]="date()" (ngModelChange)="date.set($event)" />
            </label>
            <div class="row" style="align-items:flex-end">
              <button class="btn btn-primary" (click)="load()" [disabled]="!classId() || !sectionId() || loading()">
                {{ loading() ? 'Loading…' : 'Load roster' }}
              </button>
            </div>
          </div>
        </div>
      </section>

      @if (attendance(); as a) {
        <section class="card">
          <div class="card-header">
            <div>
              <h3>{{ a.className }} – {{ a.sectionName }}</h3>
              <span class="small muted">{{ a.date | date:'fullDate' }} · {{ a.students.length }} students</span>
            </div>
            <div class="row">
              <span class="badge green">{{ presentCount() }} present</span>
              <span class="badge amber">{{ lateCount() }} late</span>
              <span class="badge red">{{ absentCount() }} absent</span>
            </div>
          </div>

          @if (!a.students.length) {
            <div class="empty">No students are enrolled in this section.</div>
          } @else {
            <div class="table-wrap">
              <table>
                <thead><tr><th>Student</th><th>Roll</th><th>Status</th><th>Remarks</th></tr></thead>
                <tbody>
                  @for (student of a.students; track student.studentId) {
                    <tr>
                      <td>
                        <div class="primary">{{ student.fullName }}</div>
                        <div class="secondary">{{ student.admissionNo }}</div>
                      </td>
                      <td>{{ student.rollNo || '—' }}</td>
                      <td style="min-width:150px">
                        <select [(ngModel)]="student.status" style="min-height:34px;border:1px solid var(--line);border-radius:8px;padding:4px 8px">
                          <option [ngValue]="null">Not marked</option>
                          @for (status of statuses; track status) { <option [value]="status">{{ status }}</option> }
                        </select>
                      </td>
                      <td>
                        <input [(ngModel)]="student.remarks" placeholder="Optional" style="min-height:34px;border:1px solid var(--line);border-radius:8px;padding:4px 8px;width:100%" />
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <div class="card-footer row">
              <button class="btn btn-primary" (click)="save()" [disabled]="saving()">
                {{ saving() ? 'Saving…' : 'Submit attendance' }}
              </button>
              <span class="small muted">Unmarked students are ignored. Submitting again updates today's record.</span>
            </div>
          }
        </section>
      } @else if (!loading()) {
        <div class="empty">Choose a class, section and date to load the roster.</div>
      }
    </div>
  `
})
export class AttendanceComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly classes = signal<ClassRoom[]>([]);
  readonly sections = signal<Section[]>([]);
  readonly attendance = signal<ClassAttendance | null>(null);
  readonly classId = signal<number | null>(null);
  readonly sectionId = signal<number | null>(null);
  readonly date = signal(iso(new Date()));
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  readonly statuses = ['Present', 'Absent', 'Late', 'HalfDay', 'Leave'];

  readonly presentCount = computed(() => this.count('Present'));
  readonly absentCount = computed(() => this.count('Absent'));
  readonly lateCount = computed(() => this.count('Late'));

  ngOnInit(): void {
    this.api.get<ClassRoom[]>('admin/classes').subscribe({
      next: classes => this.classes.set(classes),
      error: () => this.error.set('Could not load classes.')
    });
  }

  setClass(classId: number | null): void {
    this.classId.set(classId);
    this.sectionId.set(null);
    this.sections.set([]);
    this.attendance.set(null);
    if (!classId) return;

    this.api.get<Section[]>(`admin/classes/${classId}/sections`).subscribe({
      next: sections => {
        this.sections.set(sections);
        if (sections.length) this.sectionId.set(sections[0].id);
      },
      error: () => this.error.set('Could not load sections.')
    });
  }

  load(): void {
    if (!this.classId() || !this.sectionId()) return;
    this.loading.set(true);
    this.error.set(null);
    this.message.set(null);

    this.api
      .get<ClassAttendance>('attendance/roster', {
        classId: this.classId(),
        sectionId: this.sectionId(),
        date: this.date()
      })
      .subscribe({
        next: roster => {
          for (const student of roster.students) {
            if (!student.status) student.status = 'Present';
          }
          this.attendance.set(roster);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Could not load the roster for that class.');
          this.loading.set(false);
        }
      });
  }

  markAllPresent(): void {
    const roster = this.attendance();
    if (!roster) return;
    for (const student of roster.students) student.status = 'Present';
  }

  save(): void {
    const roster = this.attendance();
    if (!roster || this.saving()) return;

    const entries = roster.students
      .filter(student => student.status)
      .map(student => ({
        studentId: student.studentId,
        status: student.status,
        remarks: student.remarks || null
      }));

    if (!entries.length) {
      this.error.set('Mark at least one student before submitting.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.api
      .post<{ created: number; updated: number; notificationsQueued: number }>('attendance/mark', {
        classId: roster.classId,
        sectionId: roster.sectionId,
        date: roster.date,
        entries
      })
      .subscribe({
        next: result => {
          this.saving.set(false);
          this.message.set(
            `${result.created} records created, ${result.updated} updated, ${result.notificationsQueued} parent notifications queued.`
          );
          this.load();
        },
        error: () => {
          this.saving.set(false);
          this.error.set('Attendance could not be saved.');
        }
      });
  }

  private count(status: string): number {
    return this.attendance()?.students.filter(student => student.status === status).length ?? 0;
  }
}

function iso(date: Date): string {
  return localDate(date);
}
