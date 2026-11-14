import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import {
  AcademicSession, ClassRoom, Exam, ExamResultRow, ExamSubject, StudentMark
} from '../../core/models';
import { readError } from '../platform/schools.component';

@Component({
  selector: 'app-exams',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, DatePipe, DecimalPipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Academics</div>
          <h1>Examinations</h1>
          <p class="subtitle">Configure exams, enter marks, publish results and print report cards.</p>
        </div>
        <div class="toolbar">
          @if (auth.has('exams.manage')) {
            <button class="btn btn-primary" (click)="toggleForm()">
              {{ formOpen() ? 'Close' : '+ New exam' }}
            </button>
          }
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      @if (formOpen()) {
        <section class="card">
          <div class="card-header"><h3>{{ editingExam() ? 'Edit examination' : 'Create examination' }}</h3></div>
          <div class="card-body">
            <form class="form-grid" [formGroup]="form" (ngSubmit)="saveExam()">
              <label class="field">
                <span>Name *</span>
                <input formControlName="name" placeholder="e.g. Term 1 Assessment" />
              </label>
              <label class="field">
                <span>Type *</span>
                <select formControlName="examType">
                  @for (type of examTypes; track type) { <option [value]="type">{{ type }}</option> }
                </select>
              </label>
              <label class="field"><span>Start date *</span><input type="date" formControlName="startDate" /></label>
              <label class="field"><span>End date *</span><input type="date" formControlName="endDate" /></label>
              <label class="field full row" style="align-items:center">
                <input type="checkbox" formControlName="isRankEnabled" style="width:auto;min-height:auto" />
                <span>Enable ranks when results are published</span>
              </label>
              <div class="full">
                <button class="btn btn-primary" type="submit" [disabled]="saving() || form.invalid">
                  {{ saving() ? 'Saving…' : (editingExam() ? 'Save changes' : 'Create exam') }}
                </button>
                @if (editingExam()) {
                  <button class="btn btn-ghost" type="button" [disabled]="saving()" (click)="cancelEdit()">Cancel</button>
                }
              </div>
            </form>
          </div>
        </section>
      }

      <section class="split">
        <div style="display:grid;gap:20px">
          <article class="card">
            <div class="card-header"><h3>Examinations</h3><span class="badge">{{ exams().length }}</span></div>
            @if (!exams().length) {
              <div class="empty">No exams configured yet.</div>
            } @else {
              <div class="table-wrap">
                <table>
                  <thead><tr><th>Exam</th><th>Dates</th><th>Status</th><th></th></tr></thead>
                  <tbody>
                    @for (exam of exams(); track exam.id) {
                      <tr>
                        <td>
                          <div class="primary">{{ exam.name }}</div>
                          <div class="secondary">{{ exam.examType }} · {{ exam.subjectCount }} subjects</div>
                        </td>
                        <td class="small muted">{{ exam.startDate | date:'dd MMM' }} – {{ exam.endDate | date:'dd MMM yyyy' }}</td>
                        <td><span [class]="'badge ' + badge(exam.status)">{{ exam.status }}</span></td>
                        <td>
                          <div class="row" style="gap:6px">
                            <button class="btn btn-ghost btn-sm" (click)="selectExam(exam)">Open</button>
                            @if (auth.has('exams.manage') && exam.status === 'Draft') {
                              <button class="btn btn-ghost btn-sm" [disabled]="saving()" (click)="editExam(exam)" [attr.aria-label]="'Edit ' + exam.name">Edit</button>
                              <button class="btn btn-ghost btn-sm" [disabled]="saving()" (click)="deleteExam(exam)" [attr.aria-label]="'Delete ' + exam.name">Delete</button>
                            }
                          </div>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </article>

          @if (selected(); as exam) {
            <article class="card">
              <div class="card-header">
                <div>
                  <h3>{{ exam.name }}</h3>
                  <span class="small muted">{{ exam.examType }} · {{ exam.startDate | date:'dd MMM yyyy' }}</span>
                </div>
                <div class="row">
                  @if (auth.has('exams.manage')) {
                    @if (exam.status === 'Draft' || exam.status === 'MarksEntry') {
                      <button class="btn btn-ghost btn-sm" (click)="submitForApproval(exam)">Submit</button>
                    }
                    @if (exam.status === 'PendingApproval') {
                      <button class="btn btn-primary btn-sm" (click)="approve(exam)">Approve</button>
                    }
                    @if (exam.status === 'Approved') {
                      <button class="btn btn-gold btn-sm" (click)="publish(exam)">Publish results</button>
                    }
                  }
                </div>
              </div>
              <div class="card-body">
                <h4 style="margin-bottom:10px">Subjects</h4>
                @if (!subjects().length) {
                  <div class="empty">No subjects configured for this exam.</div>
                } @else {
                  <div class="list">
                    @for (subject of subjects(); track subject.id) {
                      <div class="list-item">
                        <div class="dot"></div>
                        <div style="flex:1">
                          <div style="font-weight:600">{{ subject.subjectName }}</div>
                          <div class="small muted">
                            {{ subject.className }} · Max {{ subject.maxTheoryMarks + subject.maxPracticalMarks }} ·
                            Pass {{ subject.passMarks }}
                          </div>
                        </div>
                        <button class="btn btn-ghost btn-sm" (click)="openMarks(subject)">Marks</button>
                      </div>
                    }
                  </div>
                }
              </div>
            </article>
          }
        </div>

        <div style="display:grid;gap:20px">
          @if (marksSubject(); as subject) {
            <article class="card">
              <div class="card-header">
                <div><h3>Marks entry</h3><span class="small muted">{{ subject.subjectName }} · {{ subject.className }}</span></div>
                <button class="btn btn-ghost btn-sm" (click)="marksSubject.set(null)">Close</button>
              </div>
              <div class="card-body">
                @if (!marks().length) {
                  <div class="empty">No students to mark for this subject.</div>
                } @else {
                  <div class="table-wrap">
                    <table style="min-width:520px">
                      <thead><tr><th>Student</th><th>Theory</th><th>Practical</th><th>Absent</th></tr></thead>
                      <tbody>
                        @for (mark of marks(); track mark.studentId) {
                          <tr>
                            <td>
                              <div class="primary">{{ mark.studentName }}</div>
                              <div class="secondary">{{ mark.admissionNo }}</div>
                            </td>
                            <td><input type="number" min="0" [(ngModel)]="mark.theoryMarks" style="width:80px;min-height:32px;border:1px solid var(--line);border-radius:7px;padding:3px 7px" /></td>
                            <td><input type="number" min="0" [(ngModel)]="mark.practicalMarks" style="width:80px;min-height:32px;border:1px solid var(--line);border-radius:7px;padding:3px 7px" /></td>
                            <td style="text-align:center"><input type="checkbox" [(ngModel)]="mark.isAbsent" /></td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                  <div class="row" style="margin-top:14px">
                    <button class="btn btn-primary" (click)="saveMarks()" [disabled]="saving()">
                      {{ saving() ? 'Saving…' : 'Save marks' }}
                    </button>
                  </div>
                }
              </div>
            </article>
          }

          @if (selected()) {
            <article class="card">
              <div class="card-header">
                <h3>Results</h3>
                <select [value]="resultClassId()" (change)="setResultClass($event)" style="min-height:32px;border:1px solid var(--line);border-radius:8px;padding:4px 8px">
                  <option value="">All classes</option>
                  @for (item of classes(); track item.id) { <option [value]="item.id">{{ item.name }}</option> }
                </select>
              </div>
              @if (!results().length) {
                <div class="empty">No marks have been recorded for this exam.</div>
              } @else {
                <div class="table-wrap">
                  <table style="min-width:520px">
                    <thead><tr><th>Student</th><th>Total</th><th>%</th><th>Grade</th><th>Rank</th><th></th></tr></thead>
                    <tbody>
                      @for (row of results(); track row.studentId) {
                        <tr>
                          <td>
                            <div class="primary">{{ row.studentName }}</div>
                            <div class="secondary">{{ row.admissionNo }}</div>
                          </td>
                          <td>{{ row.obtainedMarks | number:'1.0-1' }} / {{ row.totalMarks | number:'1.0-0' }}</td>
                          <td>{{ row.percentage | number:'1.0-1' }}%</td>
                          <td><span [class]="'badge ' + (row.resultStatus === 'Pass' ? 'green' : 'red')">{{ row.grade || row.resultStatus }}</span></td>
                          <td>{{ row.rank ?? '—' }}</td>
                          <td><button class="btn btn-ghost btn-sm" (click)="reportCard(row)">Report card</button></td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </article>
          }
        </div>
      </section>
    </div>
  `
})
export class ExamsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  readonly auth = inject(AuthService);

  readonly exams = signal<Exam[]>([]);
  readonly subjects = signal<ExamSubject[]>([]);
  readonly results = signal<ExamResultRow[]>([]);
  readonly marks = signal<StudentMark[]>([]);
  readonly classes = signal<ClassRoom[]>([]);
  readonly sessions = signal<AcademicSession[]>([]);
  readonly selected = signal<Exam | null>(null);
  readonly marksSubject = signal<ExamSubject | null>(null);
  readonly resultClassId = signal('');
  readonly formOpen = signal(false);
  readonly editingExam = signal<Exam | null>(null);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  readonly examTypes = ['UnitTest', 'MonthlyTest', 'HalfYearly', 'Annual', 'Practical', 'InternalAssessment'];

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    examType: ['UnitTest'],
    startDate: ['', Validators.required],
    endDate: ['', Validators.required],
    isRankEnabled: [true]
  });

  ngOnInit(): void {
    this.api.get<ClassRoom[]>('admin/classes').subscribe({ next: rows => this.classes.set(rows), error: () => this.error.set('Some information could not be loaded. Check your connection and refresh the page.') });
    this.api.get<AcademicSession[]>('admin/sessions').subscribe({
      next: rows => {
        this.sessions.set(rows);
        if (rows.length) this.loadExams(rows.find(s => s.isCurrent)?.id ?? rows[0].id);
      },
      error: () => this.loadExams()
    });
  }

  loadExams(sessionId?: number): void {
    this.api.get<Exam[]>('exams', { sessionId }).subscribe({
      next: exams => {
        this.exams.set(exams);
        if (exams.length && !this.selected()) this.selectExam(exams[0]);
      },
      error: () => this.error.set('Could not load exams.')
    });
  }

  selectExam(exam: Exam): void {
    this.selected.set(exam);
    this.marksSubject.set(null);
    this.marks.set([]);
    this.loadSubjects(exam.id);
    this.loadResults(exam.id);
  }

  loadSubjects(examId: number): void {
    this.api.get<ExamSubject[]>(`exams/${examId}/subjects`).subscribe({
      next: rows => this.subjects.set(rows),
      error: () => this.subjects.set([])
    });
  }

  loadResults(examId: number): void {
    this.api.get<ExamResultRow[]>(`exams/${examId}/results`, { classId: this.resultClassId() }).subscribe({
      next: rows => this.results.set(rows),
      error: () => this.results.set([])
    });
  }

  setResultClass(event: Event): void {
    this.resultClassId.set((event.target as HTMLSelectElement).value);
    const exam = this.selected();
    if (exam) this.loadResults(exam.id);
  }

  openMarks(subject: ExamSubject): void {
    this.marksSubject.set(subject);
    this.marks.set([]);
    this.api.get<StudentMark[]>(`exams/subjects/${subject.id}/marks`).subscribe({
      next: rows => this.marks.set(rows),
      error: () => this.error.set('Could not load marks for this subject.')
    });
  }

  saveMarks(): void {
    const exam = this.selected();
    const subject = this.marksSubject();
    if (!exam || !subject || this.saving()) return;

    this.saving.set(true);
    this.api
      .post<{ saved: number }>(`exams/${exam.id}/marks`, {
        examSubjectId: subject.id,
        marks: this.marks().map(mark => ({
          studentId: mark.studentId,
          theoryMarks: mark.theoryMarks,
          practicalMarks: mark.practicalMarks,
          isAbsent: mark.isAbsent,
          remarks: mark.remarks
        }))
      })
      .subscribe({
        next: result => {
          this.saving.set(false);
          this.message.set(`${result.saved} mark records saved.`);
          this.loadResults(exam.id);
        },
        error: () => {
          this.saving.set(false);
          this.error.set('Marks could not be saved. Check the maximum marks.');
        }
      });
  }

  toggleForm(): void {
    if (this.formOpen()) { this.cancelEdit(); return; }
    this.formOpen.set(true);
  }

  saveExam(): void {
    const editing = this.editingExam();
    if (editing) this.updateExam(editing);
    else this.createExam();
  }

  editExam(exam: Exam): void {
    this.editingExam.set(exam);
    this.form.reset({
      name: exam.name, examType: exam.examType, startDate: exam.startDate.slice(0, 10),
      endDate: exam.endDate.slice(0, 10), isRankEnabled: exam.isRankEnabled
    });
    this.formOpen.set(true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  cancelEdit(): void {
    this.editingExam.set(null);
    this.formOpen.set(false);
    this.form.reset({ name: '', examType: 'UnitTest', startDate: '', endDate: '', isRankEnabled: true });
  }

  updateExam(exam: Exam): void {
    if (this.form.invalid || this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.api.put<Exam>(`exams/${exam.id}`, this.form.getRawValue()).subscribe({
      next: updated => {
        this.saving.set(false);
        this.message.set(`${updated.name} updated.`);
        this.cancelEdit();
        if (this.selected()?.id === updated.id) this.selected.set(updated);
        this.loadExams(updated.academicSessionId);
      },
      error: r => {
        this.saving.set(false);
        this.message.set(null);
        this.error.set(readError(r));
      }
    });
  }

  deleteExam(exam: Exam): void {
    if (this.saving() || !window.confirm(`Delete ${exam.name}? Its subject configuration will also be removed.`)) return;
    this.saving.set(true);
    this.error.set(null);
    this.api.delete(`exams/${exam.id}`).subscribe({
      next: () => {
        this.saving.set(false);
        this.message.set(`${exam.name} deleted.`);
        if (this.editingExam()?.id === exam.id) this.cancelEdit();
        if (this.selected()?.id === exam.id) {
          this.selected.set(null);
          this.subjects.set([]);
          this.results.set([]);
          this.marksSubject.set(null);
        }
        this.loadExams(exam.academicSessionId);
      },
      error: r => {
        this.saving.set(false);
        this.message.set(null);
        this.error.set(readError(r));
      }
    });
  }

  createExam(): void {
    if (this.form.invalid || this.saving()) return;
    const session = this.sessions().find(item => item.isCurrent) ?? this.sessions()[0];
    if (!session) {
      this.error.set('Create an academic session before adding exams.');
      return;
    }

    this.saving.set(true);
    this.api
      .post<Exam>('exams', { ...this.form.getRawValue(), academicSessionId: session.id })
      .subscribe({
        next: exam => {
          this.saving.set(false);
          this.formOpen.set(false);
          this.message.set(`${exam.name} created.`);
          this.exams.update(rows => [exam, ...rows]);
          this.selectExam(exam);
        },
        error: () => {
          this.saving.set(false);
          this.error.set('Could not create the exam.');
        }
      });
  }

  submitForApproval(exam: Exam): void {
    this.api.post(`exams/${exam.id}/submit`).subscribe({
      next: () => {
        this.message.set(`${exam.name} submitted for approval.`);
        this.loadExams();
      },
      error: () => this.error.set('Could not submit the exam for approval.')
    });
  }

  approve(exam: Exam): void {
    this.api.post(`exams/${exam.id}/approve`).subscribe({
      next: () => {
        this.message.set(`${exam.name} approved.`);
        this.loadExams();
      },
      error: () => this.error.set('Could not approve the exam.')
    });
  }

  publish(exam: Exam): void {
    if (!window.confirm(`Publish results for ${exam.name}? Report cards will be generated.`)) return;
    this.api.post<{ reportCards: number }>(`exams/${exam.id}/publish`).subscribe({
      next: result => {
        this.message.set(`${result.reportCards} report cards generated and results published.`);
        this.loadExams();
      },
      error: () => this.error.set('Could not publish results. Ensure all marks are entered.')
    });
  }

  reportCard(row: ExamResultRow): void {
    const exam = this.selected();
    if (!exam) return;

    this.api.get<{ html?: string | null; studentName: string }>(`exams/${exam.id}/report-cards/${row.studentId}`).subscribe({
      next: card => {
        if (!card.html) {
          this.error.set('Report card is not available yet.');
          return;
        }
        const blob = new Blob([card.html], { type: 'text/html' });
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank', 'noopener');
      },
      error: () => this.error.set('Could not generate the report card.')
    });
  }

  badge(status: string): string {
    if (status === 'Published' || status === 'Approved') return 'green';
    if (status === 'PendingApproval') return 'amber';
    if (status === 'Closed') return 'red';
    return 'teal';
  }
}
