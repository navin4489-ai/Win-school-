import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { readError } from '../platform/schools.component';

interface ClassRoom { id: number; name: string; }
interface Section { id: number; classId: number; name: string; }
interface Subject { id: number; name: string; code: string; }
interface Homework {
  id: number; classId: number; sectionId: number; subjectName: string; teacherName: string;
  title: string; description?: string | null; attachmentUrl?: string | null; dueAtUtc: string;
  maxMarks?: number | null; submissionCount: number; isOverdue: boolean; isPublished: boolean; employeeId: number;
}
interface Submission {
  id: number; studentName: string; submittedAtUtc: string; fileUrl?: string | null; remarks?: string | null;
  marks?: number | null; feedback?: string | null; isLate: boolean;
}

/** Homework: teachers post and grade, students submit, parents follow along. */
@Component({
  selector: 'app-homework',
  standalone: true,
  imports: [ReactiveFormsModule, FormsModule, DatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Academics</div>
          <h1>Homework</h1>
          <p class="subtitle">{{ canManage() ? 'Post homework for your classes and grade submissions.' : 'Homework set for your class.' }}</p>
        </div>
        <div class="toolbar">
          @if (canManage()) {
            <button class="btn btn-primary btn-sm" (click)="toggleForm()">{{ formOpen() ? 'Close' : 'Add homework' }}</button>
          }
          <button class="btn btn-ghost btn-sm" (click)="load()">Refresh</button>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      @if (formOpen() && canManage()) {
        <section class="card">
          <div class="card-header"><h3>{{ editing() ? 'Edit homework' : 'Add homework' }}</h3></div>
          <div class="card-body">
            <form class="form-grid" [formGroup]="form" (ngSubmit)="save()">
              @if (editing(); as e) {
                <div class="full small muted">{{ placement(e) }} - {{ e.subjectName }} (class, section and subject cannot be changed)</div>
              } @else {
              <label class="field"><span>Class *</span>
                <select formControlName="classId" (change)="classChanged()">
                  <option value="">Select class</option>
                  @for (c of classes(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
                </select>
              </label>
              <label class="field"><span>Section *</span>
                <select formControlName="sectionId">
                  <option value="">Select section</option>
                  @for (s of sections(); track s.id) { <option [value]="s.id">{{ s.name }}</option> }
                </select>
              </label>
              <label class="field"><span>Subject *</span>
                <select formControlName="subjectId">
                  <option value="">Select subject</option>
                  @for (s of subjects(); track s.id) { <option [value]="s.id">{{ s.name }}</option> }
                </select>
              </label>
              }
              <label class="field"><span>Title *</span><input formControlName="title" placeholder="e.g. Chapter 3 exercises" /></label>
              <label class="field full"><span>Description</span>
                <textarea formControlName="description" rows="3" placeholder="What students need to do"></textarea>
              </label>
              <label class="field"><span>Due date & time *</span><input formControlName="dueAt" type="datetime-local" /></label>
              <label class="field"><span>Max marks</span><input formControlName="maxMarks" type="number" min="0" placeholder="Optional" /></label>
              <label class="field"><span>Attachment link</span><input formControlName="attachmentUrl" placeholder="https://… (optional)" /></label>
              <label class="row" style="gap:8px"><input type="checkbox" formControlName="isPublished" /><span>Publish now (visible to students and parents)</span></label>
              <div class="full row">
                <button class="btn btn-primary" type="submit" [disabled]="form.invalid || saving()">{{ saving() ? 'Saving…' : (editing() ? 'Save changes' : 'Post homework') }}</button>
                @if (editing()) { <button class="btn btn-ghost" type="button" [disabled]="saving()" (click)="cancelEdit()">Cancel</button> }
              </div>
            </form>
          </div>
        </section>
      }

      <section class="card">
        <div class="card-header"><h3>Homework ({{ items().length }})</h3></div>
        @if (loading()) {
          <div class="loading">Loading…</div>
        } @else if (!items().length) {
          <div class="empty">No homework yet.{{ canManage() ? ' Use "Add homework" to post the first one.' : '' }}</div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead><tr><th>Title</th><th>Class</th><th>Subject</th><th>Teacher</th><th>Due</th><th>Submissions</th><th></th></tr></thead>
              <tbody>
                @for (h of items(); track h.id) {
                  <tr>
                    <td>
                      <div class="primary">{{ h.title }} @if (!h.isPublished) { <span class="badge amber">Draft</span> }</div>
                      @if (h.description) { <div class="small muted">{{ h.description }}</div> }
                      @if (h.attachmentUrl) { <a class="small" [href]="h.attachmentUrl" target="_blank" rel="noopener">Attachment</a> }
                    </td>
                    <td>{{ placement(h) }}</td>
                    <td>{{ h.subjectName }}</td>
                    <td>{{ h.teacherName }}</td>
                    <td>
                      {{ asUtc(h.dueAtUtc) | date:'dd MMM, h:mm a' }}
                      @if (h.isOverdue) { <span class="badge">Overdue</span> }
                    </td>
                    <td>{{ h.submissionCount }}</td>
                    <td>
                      @if (canManage()) {
                        <button class="btn btn-ghost btn-sm" (click)="toggleSubmissions(h)">{{ openId() === h.id ? 'Hide' : 'Submissions' }}</button>
                        @if (canEdit(h)) {
                          @if (!h.isPublished) {
                            <button class="btn btn-primary btn-sm" [disabled]="saving()" (click)="publish(h)">Publish</button>
                          }
                          <button class="btn btn-ghost btn-sm" [disabled]="saving()" (click)="edit(h)" [attr.aria-label]="'Edit ' + h.title">Edit</button>
                          <button class="btn btn-ghost btn-sm" [disabled]="saving()" (click)="remove(h)" [attr.aria-label]="'Delete ' + h.title">Delete</button>
                        }
                      } @else if (isStudent()) {
                        <button class="btn btn-ghost btn-sm" (click)="openId.set(openId() === h.id ? null : h.id)">Submit</button>
                      }
                    </td>
                  </tr>
                  @if (openId() === h.id && canManage()) {
                    <tr><td colspan="7">
                      @if (!submissions().length) {
                        <div class="small muted">No submissions yet.</div>
                      }
                      @for (s of submissions(); track s.id) {
                        <div class="row" style="gap:10px;flex-wrap:wrap;padding:6px 0;border-bottom:1px solid var(--line)">
                          <strong style="min-width:140px">{{ s.studentName }}</strong>
                          <span class="small muted">{{ asUtc(s.submittedAtUtc) | date:'dd MMM, h:mm a' }}{{ s.isLate ? ' · late' : '' }}</span>
                          @if (s.remarks) { <span class="small">{{ s.remarks }}</span> }
                          @if (s.fileUrl) { <a class="small" [href]="s.fileUrl" target="_blank" rel="noopener">File</a> }
                          <input type="number" min="0" aria-label="Marks" [(ngModel)]="s.marks" placeholder="Marks" style="width:80px" />
                          <input [(ngModel)]="s.feedback" aria-label="Feedback" placeholder="Feedback" style="width:100%;max-width:180px" />
                          <button class="btn btn-primary btn-sm" [disabled]="saving()" (click)="grade(s)">Save grade</button>
                        </div>
                      }
                    </td></tr>
                  }
                  @if (openId() === h.id && isStudent()) {
                    <tr><td colspan="7">
                      <div class="row" style="gap:8px;flex-wrap:wrap">
                        <input [(ngModel)]="submitRemarks" aria-label="Your answer" placeholder="Your answer / remarks" style="flex:1;min-width:0" />
                        <input [(ngModel)]="submitLink" aria-label="Link to your file" placeholder="Link to your file (optional)" style="width:100%;max-width:220px" />
                        <button class="btn btn-primary btn-sm" [disabled]="saving()" (click)="submit(h)">Submit</button>
                      </div>
                    </td></tr>
                  }
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </div>
  `
})
export class HomeworkComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);

  readonly canManage = computed(() => this.auth.has('homework.manage'));
  readonly isStudent = computed(() => this.auth.user()?.roleCode === 'Student');
  readonly isAdmin = computed(() => ['Principal', 'Administrator', 'SchoolOwner', 'SuperAdmin'].includes(this.auth.user()?.roleCode ?? ''));
  readonly editing = signal<Homework | null>(null);

  readonly items = signal<Homework[]>([]);
  readonly classes = signal<ClassRoom[]>([]);
  readonly sections = signal<Section[]>([]);
  readonly subjects = signal<Subject[]>([]);
  readonly submissions = signal<Submission[]>([]);
  readonly sectionNames = signal<Record<number, string>>({});
  readonly openId = signal<number | null>(null);
  readonly formOpen = signal(false);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  submitRemarks = '';
  submitLink = '';

  readonly form = this.fb.nonNullable.group({
    classId: ['', Validators.required],
    sectionId: ['', Validators.required],
    subjectId: ['', Validators.required],
    title: ['', Validators.required],
    description: [''],
    dueAt: [defaultDue(), Validators.required],
    maxMarks: [''],
    attachmentUrl: [''],
    isPublished: [true]
  });

  ngOnInit(): void {
    this.load();
    this.api.get<ClassRoom[]>('admin/classes').subscribe({
      next: c => {
        this.classes.set(c);
        c.forEach(cls => this.api.get<Section[]>(`admin/classes/${cls.id}/sections`).subscribe({
          next: secs => this.sectionNames.update(all => ({ ...all, ...Object.fromEntries(secs.map(s => [s.id, s.name])) }))
        }));
      }
    });
    this.api.get<Subject[]>('admin/subjects').subscribe({ next: s => this.subjects.set(s), error: (r: unknown) => this.error.set(readError(r)) });
  }

  load(): void {
    this.loading.set(true);
    this.api.get<Homework[]>('homework', { includeUnpublished: this.canManage() || undefined }).subscribe({
      next: items => { this.items.set(items); this.loading.set(false); },
      error: r => { this.error.set(readError(r)); this.loading.set(false); }
    });
  }

  asUtc(value: string): string {
    return /Z|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z';
  }

  placement(h: Homework): string {
    const cls = this.classes().find(c => c.id === h.classId)?.name ?? '';
    const sec = this.sectionNames()[h.sectionId] ?? '';
    return [cls, sec].filter(Boolean).join(' - ') || '—';
  }

  classChanged(): void {
    const classId = Number(this.form.controls.classId.value);
    this.form.controls.sectionId.setValue('');
    this.sections.set([]);
    if (!classId) return;
    this.api.get<Section[]>(`admin/classes/${classId}/sections`).subscribe({ next: s => this.sections.set(s), error: (r: unknown) => this.error.set(readError(r)) });
  }

  save(): void {
    if (this.form.invalid || this.saving()) return;
    const editing = this.editing();
    if (editing) { this.saveEdit(editing); return; }
    const raw = this.form.getRawValue();
    const body = {
      classId: Number(raw.classId), sectionId: Number(raw.sectionId), subjectId: Number(raw.subjectId),
      title: raw.title.trim(), description: raw.description || null,
      attachmentUrl: raw.attachmentUrl.trim() || null,
      dueAtUtc: new Date(raw.dueAt).toISOString(),
      maxMarks: raw.maxMarks === '' ? null : Number(raw.maxMarks),
      isPublished: raw.isPublished
    };
    this.saving.set(true);
    this.error.set(null);
    this.api.post<Homework>('homework', body).subscribe({
      next: () => {
        this.saving.set(false);
        this.message.set(`Homework "${body.title}" posted.`);
        this.form.reset({ classId: raw.classId, sectionId: raw.sectionId, subjectId: raw.subjectId, title: '', description: '',
          dueAt: defaultDue(), maxMarks: '', attachmentUrl: '', isPublished: true });
        this.load();
      },
      error: r => { this.saving.set(false); this.message.set(null); this.error.set(readError(r)); }
    });
  }

  toggleForm(): void {
    if (this.formOpen()) { this.cancelEdit(); return; }
    this.formOpen.set(true);
  }

  canEdit(h: Homework): boolean {
    if (!this.canManage()) return false;
    if (this.isAdmin()) return true;
    const employeeId = this.auth.user()?.employeeId;
    return employeeId == null || employeeId === h.employeeId;
  }

  edit(h: Homework): void {
    this.editing.set(h);
    this.formOpen.set(true);
    this.form.controls.classId.disable();
    this.form.controls.sectionId.disable();
    this.form.controls.subjectId.disable();
    this.form.reset({
      classId: String(h.classId), sectionId: String(h.sectionId), subjectId: '',
      title: h.title, description: h.description ?? '', dueAt: toLocalInput(this.asUtc(h.dueAtUtc)),
      maxMarks: h.maxMarks == null ? '' : String(h.maxMarks), attachmentUrl: h.attachmentUrl ?? '',
      isPublished: h.isPublished
    });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  cancelEdit(): void {
    this.editing.set(null);
    this.formOpen.set(false);
    this.form.controls.classId.enable();
    this.form.controls.sectionId.enable();
    this.form.controls.subjectId.enable();
    this.form.reset({ classId: '', sectionId: '', subjectId: '', title: '', description: '',
      dueAt: defaultDue(), maxMarks: '', attachmentUrl: '', isPublished: true });
  }

  publish(h: Homework): void {
    this.update(h, {
      title: h.title, description: h.description ?? null, attachmentUrl: h.attachmentUrl ?? null,
      dueAtUtc: this.asUtc(h.dueAtUtc), maxMarks: h.maxMarks ?? null, isPublished: true
    }, `"${h.title}" published.`);
  }

  remove(h: Homework): void {
    if (this.saving() || !window.confirm(`Delete homework "${h.title}"? Ungraded submissions will also be removed.`)) return;
    this.saving.set(true);
    this.error.set(null);
    this.api.delete(`homework/${h.id}`).subscribe({
      next: () => {
        this.saving.set(false);
        this.message.set(`Homework "${h.title}" deleted.`);
        if (this.openId() === h.id) this.openId.set(null);
        if (this.editing()?.id === h.id) this.cancelEdit();
        this.load();
      },
      error: r => { this.saving.set(false); this.message.set(null); this.error.set(readError(r)); }
    });
  }

  private saveEdit(h: Homework): void {
    const raw = this.form.getRawValue();
    this.update(h, {
      title: raw.title.trim(), description: raw.description || null,
      attachmentUrl: raw.attachmentUrl.trim() || null,
      dueAtUtc: new Date(raw.dueAt).toISOString(),
      maxMarks: raw.maxMarks === '' || raw.maxMarks == null ? null : Number(raw.maxMarks),
      isPublished: raw.isPublished
    }, `Homework "${raw.title.trim()}" updated.`, () => this.cancelEdit());
  }

  private update(h: Homework, body: Record<string, unknown>, success: string, after?: () => void): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.api.put<Homework>(`homework/${h.id}`, body).subscribe({
      next: () => { this.saving.set(false); this.message.set(success); after?.(); this.load(); },
      error: r => { this.saving.set(false); this.message.set(null); this.error.set(readError(r)); }
    });
  }

  toggleSubmissions(h: Homework): void {
    if (this.openId() === h.id) { this.openId.set(null); return; }
    this.openId.set(h.id);
    this.submissions.set([]);
    this.api.get<Submission[]>(`homework/${h.id}/submissions`).subscribe({
      next: s => this.submissions.set(s),
      error: r => this.error.set(readError(r))
    });
  }

  grade(s: Submission): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.api.post(`homework/submissions/${s.id}/grade`, { marks: Number(s.marks) || 0, feedback: s.feedback || null }).subscribe({
      next: () => { this.saving.set(false); this.error.set(null); this.message.set(`Grade saved for ${s.studentName}.`); },
      error: r => { this.saving.set(false); this.error.set(readError(r)); }
    });
  }

  submit(h: Homework): void {
    if (this.saving()) return;
    if (!this.submitRemarks.trim() && !this.submitLink.trim()) {
      this.error.set('Write your answer or add a link to your file before submitting.');
      return;
    }
    this.saving.set(true);
    this.api.post(`homework/${h.id}/submissions`, { remarks: this.submitRemarks || null, fileUrl: this.submitLink.trim() || null }).subscribe({
      next: () => {
        this.saving.set(false);
        this.error.set(null);
        this.message.set(`Submitted "${h.title}".`);
        this.submitRemarks = '';
        this.submitLink = '';
        this.openId.set(null);
        this.load();
      },
      error: r => { this.saving.set(false); this.error.set(readError(r)); }
    });
  }
}

function toLocalInput(value: string): string {
  const d = new Date(value);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function defaultDue(): string {
  const d = new Date(Date.now() + 24 * 3600 * 1000);
  d.setHours(18, 0, 0, 0);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
