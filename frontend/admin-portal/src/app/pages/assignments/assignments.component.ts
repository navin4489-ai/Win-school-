import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

interface ClassRoom { id: number; name: string; code: string; }
interface Section { id: number; classId: number; name: string; classTeacherId?: number | null; studentCount: number; }
interface Subject { id: number; name: string; code: string; }
interface Employee { id: number; fullName: string; employeeNo: string; designation?: string | null; }
interface TeacherAssignment {
  id: number;
  employeeId: number;
  employeeName: string;
  employeeNo: string;
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  subjectId?: number | null;
  subjectName?: string | null;
  isClassTeacher: boolean;
}

/**
 * Class and teacher assignments. The principal decides which teacher owns which
 * class and section; the assignment is what authorises a teacher to mark attendance,
 * enter marks and publish homework for that scope.
 */
@Component({
  selector: 'app-assignments',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Academics</div>
          <h1>Class &amp; teacher assignments</h1>
          <p class="subtitle">Assign teachers to classes and sections, nominate the class teacher and give subject-wise scope.</p>
        </div>
        <div class="toolbar">
          <button class="btn btn-primary btn-sm" (click)="formOpen.set(!formOpen())">
            {{ formOpen() ? 'Close' : 'New assignment' }}
          </button>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      @if (formOpen()) {
        <section class="card">
          <div class="card-header"><h3>{{ editingId() ? 'Edit assignment' : 'New assignment' }}</h3></div>
          <div class="card-body">
            <form class="form-grid" [formGroup]="form" (ngSubmit)="save()">
              <label class="field">
                <span>Teacher *</span>
                <select formControlName="employeeId">
                  <option value="">Select a teacher</option>
                  @for (teacher of teachers(); track teacher.id) {
                    <option [value]="teacher.id">{{ teacher.fullName }} ({{ teacher.employeeNo }})</option>
                  }
                </select>
              </label>

              <label class="field">
                <span>Class *</span>
                <select formControlName="classId" (change)="onClassChange()">
                  <option value="">Select a class</option>
                  @for (item of classes(); track item.id) {
                    <option [value]="item.id">{{ item.name }}</option>
                  }
                </select>
              </label>

              <label class="field">
                <span>Section *</span>
                <select formControlName="sectionId">
                  <option value="">Select a section</option>
                  @for (item of sections(); track item.id) {
                    <option [value]="item.id">{{ item.name }}</option>
                  }
                </select>
              </label>

              <label class="field">
                <span>Subject (optional)</span>
                <select formControlName="subjectId">
                  <option value="">All subjects</option>
                  @for (item of subjects(); track item.id) {
                    <option [value]="item.id">{{ item.name }}</option>
                  }
                </select>
                <span class="hint">Leave empty to give the teacher the whole section.</span>
              </label>

              <label class="row full" style="gap:8px">
                <input type="checkbox" formControlName="isClassTeacher" />
                <span>Nominate as class teacher</span>
              </label>

              <div class="full row">
                <button class="btn btn-primary" type="submit" [disabled]="saving() || form.invalid">
                  {{ saving() ? 'Saving…' : (editingId() ? 'Update assignment' : 'Create assignment') }}
                </button>
                @if (editingId()) {
                  <button class="btn btn-ghost" type="button" (click)="resetForm()">Cancel</button>
                }
              </div>
            </form>
          </div>
        </section>
      }

      <section class="card">
        <div class="card-header">
          <h3>Current assignments</h3>
          <div class="row">
            <select class="btn btn-ghost btn-sm" [value]="filterClassId()" (change)="onFilterChange($event)">
              <option value="">All classes</option>
              @for (item of classes(); track item.id) {
                <option [value]="item.id">{{ item.name }}</option>
              }
            </select>
            <button class="btn btn-ghost btn-sm" (click)="load()">Refresh</button>
          </div>
        </div>

        @if (loading()) {
          <div class="loading">Loading assignments…</div>
        } @else if (!assignments().length) {
          <div class="empty">No teacher has been assigned yet.</div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr><th>Teacher</th><th>Class</th><th>Section</th><th>Subject</th><th>Role</th><th></th></tr>
              </thead>
              <tbody>
                @for (item of assignments(); track item.id) {
                  <tr>
                    <td>
                      <div class="primary">{{ item.employeeName }}</div>
                      <div class="secondary">{{ item.employeeNo }}</div>
                    </td>
                    <td>{{ item.className }}</td>
                    <td>{{ item.sectionName }}</td>
                    <td>{{ item.subjectName || 'All subjects' }}</td>
                    <td>
                      @if (item.isClassTeacher) {
                        <span class="badge teal">Class teacher</span>
                      } @else {
                        <span class="badge">Teacher</span>
                      }
                    </td>
                    <td>
                      <div class="row" style="gap:6px;justify-content:flex-end">
                        <button class="btn btn-ghost btn-sm" (click)="edit(item)">Edit</button>
                        <button class="btn btn-danger btn-sm" (click)="remove(item)">Remove</button>
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </div>
  `
})
export class AssignmentsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  readonly auth = inject(AuthService);

  readonly classes = signal<ClassRoom[]>([]);
  readonly sections = signal<Section[]>([]);
  readonly subjects = signal<Subject[]>([]);
  readonly teachers = signal<Employee[]>([]);
  readonly assignments = signal<TeacherAssignment[]>([]);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly formOpen = signal(false);
  readonly editingId = signal<number | null>(null);
  readonly filterClassId = signal<string>('');
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    employeeId: ['', Validators.required],
    classId: ['', Validators.required],
    sectionId: ['', Validators.required],
    subjectId: [''],
    isClassTeacher: [false]
  });

  ngOnInit(): void {
    this.loadLookups();
    this.load();
  }

  loadLookups(): void {
    this.api.get<ClassRoom[]>('admin/classes').subscribe({
      next: items => this.classes.set(items),
      error: () => this.error.set('Could not load the class list.')
    });

    this.api.get<Subject[]>('admin/subjects').subscribe({
      next: items => this.subjects.set(items),
      error: () => this.error.set('Could not load the subject list.')
    });

    this.api.get<Employee[]>('hr/employees').subscribe({
      next: items => this.teachers.set(items),
      error: () => this.error.set('Could not load the employee list.')
    });
  }

  load(): void {
    this.loading.set(true);
    this.api.get<TeacherAssignment[]>('admin/teacher-assignments', {
      classId: this.filterClassId() || undefined
    }).subscribe({
      next: items => {
        this.assignments.set(items);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load the assignments.');
        this.loading.set(false);
      }
    });
  }

  onClassChange(): void {
    const classId = Number(this.form.controls.classId.value);
    this.form.controls.sectionId.setValue('');
    this.sections.set([]);

    if (!classId) return;

    this.api.get<Section[]>(`admin/classes/${classId}/sections`).subscribe({
      next: items => this.sections.set(items),
      error: () => this.error.set('Could not load the sections of that class.')
    });
  }

  onFilterChange(event: Event): void {
    this.filterClassId.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  save(): void {
    if (this.form.invalid || this.saving()) return;

    this.saving.set(true);
    this.error.set(null);
    this.message.set(null);

    const raw = this.form.getRawValue();
    const body = {
      employeeId: Number(raw.employeeId),
      classId: Number(raw.classId),
      sectionId: Number(raw.sectionId),
      subjectId: raw.subjectId ? Number(raw.subjectId) : null,
      isClassTeacher: raw.isClassTeacher
    };

    const editing = this.editingId();
    const request = editing
      ? this.api.put<TeacherAssignment>(`admin/teacher-assignments/${editing}`, body)
      : this.api.post<TeacherAssignment>('admin/teacher-assignments', body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.formOpen.set(false);
        this.resetForm();
        this.message.set(editing ? 'Assignment updated.' : 'Assignment created.');
        this.load();
      },
      error: response => {
        this.saving.set(false);
        this.error.set(readError(response));
      }
    });
  }

  edit(item: TeacherAssignment): void {
    this.editingId.set(item.id);
    this.form.patchValue({
      employeeId: String(item.employeeId),
      classId: String(item.classId),
      sectionId: String(item.sectionId),
      subjectId: item.subjectId ? String(item.subjectId) : '',
      isClassTeacher: item.isClassTeacher
    });

    this.api.get<Section[]>(`admin/classes/${item.classId}/sections`).subscribe({
      next: items => this.sections.set(items)
    });

    this.formOpen.set(true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  remove(item: TeacherAssignment): void {
    if (!confirm(`Remove ${item.employeeName} from ${item.className} · ${item.sectionName}?`)) return;

    this.api.delete(`admin/teacher-assignments/${item.id}`).subscribe({
      next: () => {
        this.message.set('Assignment removed.');
        this.load();
      },
      error: response => this.error.set(readError(response))
    });
  }

  resetForm(): void {
    this.editingId.set(null);
    this.form.reset({ employeeId: '', classId: '', sectionId: '', subjectId: '', isClassTeacher: false });
    this.sections.set([]);
  }
}

function readError(response: unknown): string {
  const body = (response as { error?: { detail?: string; title?: string } })?.error;
  if (!body) return 'The request could not be completed.';
  return body.detail ?? body.title ?? 'The request could not be completed.';
}
