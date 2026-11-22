import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Admission, AcademicSession, ClassRoom, PagedResult, Section } from '../../core/models';

@Component({
  selector: 'app-admissions',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, DatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Admissions</div>
          <h1>Admissions pipeline</h1>
          <p class="subtitle">Track enquiries from first contact through document checks, interviews and enrolment.</p>
        </div>
        <div class="toolbar">
          <span class="badge teal">{{ total() }} applications</span>
          <button class="btn btn-primary" (click)="formOpen.set(!formOpen())">
            {{ formOpen() ? 'Close form' : '+ New enquiry' }}
          </button>
        </div>
      </header>

      @if (message()) { <div class="success">{{ message() }}</div> }
      @if (error()) { <div class="error">{{ error() }}</div> }

      @if (formOpen()) {
        <section class="card">
          <div class="card-header"><h3>New enquiry</h3><span class="small muted">Fields marked * are required</span></div>
          <div class="card-body">
            <form class="form-grid" [formGroup]="form" (ngSubmit)="create()">
              <label class="field">
                <span>First name *</span>
                <input formControlName="firstName" />
              </label>
              <label class="field">
                <span>Last name *</span>
                <input formControlName="lastName" />
              </label>
              <label class="field">
                <span>Date of birth *</span>
                <input type="date" formControlName="dateOfBirth" />
              </label>
              <label class="field">
                <span>Gender</span>
                <select formControlName="gender">
                  <option value="Male">Male</option>
                  <option value="Female">Female</option>
                  <option value="Other">Other</option>
                </select>
              </label>
              <label class="field">
                <span>Guardian name *</span>
                <input formControlName="guardianName" />
              </label>
              <label class="field">
                <span>Guardian phone *</span>
                <input formControlName="guardianPhone" placeholder="+91…" />
              </label>
              <label class="field">
                <span>Guardian email</span>
                <input type="email" formControlName="guardianEmail" />
              </label>
              <label class="field">
                <span>Applying for class *</span>
                <select formControlName="classId">
                  <option [ngValue]="null">Select class</option>
                  @for (item of classes(); track item.id) {
                    <option [ngValue]="item.id">{{ item.name }}</option>
                  }
                </select>
              </label>
              <label class="field">
                <span>Previous school</span>
                <input formControlName="previousSchool" />
              </label>
              <label class="field">
                <span>Source</span>
                <select formControlName="source">
                  <option value="Walk-in">Walk-in</option>
                  <option value="Website">Website</option>
                  <option value="Referral">Referral</option>
                  <option value="Campaign">Campaign</option>
                  <option value="Phone">Phone</option>
                </select>
              </label>
              <label class="field full">
                <span>Notes</span>
                <textarea formControlName="notes" placeholder="Anything the admissions team should know"></textarea>
              </label>
              <div class="full row">
                <button class="btn btn-primary" type="submit" [disabled]="saving() || form.invalid">
                  {{ saving() ? 'Saving…' : 'Create enquiry' }}
                </button>
                <button class="btn btn-ghost" type="button" (click)="formOpen.set(false)">Cancel</button>
              </div>
            </form>
          </div>
        </section>
      }

      <section class="card">
        <div class="card-header">
          <div class="filters" style="flex:1">
            <input placeholder="Search name, application no. or phone" [value]="search()" (input)="setSearch($event)" />
            <select [value]="status()" (change)="setStatus($event)">
              <option value="">All statuses</option>
              @for (item of statuses; track item) { <option [value]="item">{{ item }}</option> }
            </select>
            <button class="btn btn-ghost btn-sm" type="button" (click)="load()">Refresh</button>
          </div>
        </div>

        @if (loading()) {
          <div class="loading">Loading applications…</div>
        } @else if (!rows().length) {
          <div class="empty">No applications match these filters.</div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Applicant</th>
                  <th>Class</th>
                  <th>Guardian</th>
                  <th>Applied</th>
                  <th>Status</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                @for (row of rows(); track row.id) {
                  <tr>
                    <td>
                      <div class="primary">{{ row.firstName }} {{ row.lastName }}</div>
                      <div class="secondary">{{ row.applicationNo }} · {{ row.gender }}</div>
                    </td>
                    <td>{{ row.className || '—' }}</td>
                    <td>
                      <div>{{ row.guardianName }}</div>
                      <div class="secondary">{{ row.guardianPhone }}</div>
                    </td>
                    <td class="small muted">{{ row.appliedAtUtc | date:'dd MMM yyyy' }}</td>
                    <td><span [class]="'badge ' + badge(row.status)">{{ row.status }}</span></td>
                    <td>
                      <div class="row">
                        @if (row.status === 'Enquiry') {
                          <button class="btn btn-ghost btn-sm" (click)="action(row, 'submit')">Submit</button>
                        }
                        @if (row.status === 'Application') {
                          <button class="btn btn-ghost btn-sm" (click)="action(row, 'verify-documents')">Verify docs</button>
                        }
                        @if (row.status === 'DocumentsVerified' || row.status === 'InterviewScheduled' || row.status === 'Waitlisted') {
                          <button class="btn btn-primary btn-sm" (click)="action(row, 'approve')">Approve</button>
                        }
                        @if (row.status === 'Approved') {
                          <button class="btn btn-gold btn-sm" (click)="startEnroll(row)">Enrol</button>
                        }
                        @if (row.status === 'Enrolled' && row.studentId) {
                          <a class="btn btn-ghost btn-sm" [routerLink]="['/students', row.studentId]">View student</a>
                        }
                        @if (row.status !== 'Enrolled' && row.status !== 'Rejected') {
                          <button class="btn btn-danger btn-sm" (click)="reject(row)">Reject</button>
                        }
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <div class="card-footer pagination">
            <span class="small muted">Page {{ page() }} · {{ total() }} total</span>
            <div class="row">
              <button class="btn btn-ghost btn-sm" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</button>
              <button class="btn btn-ghost btn-sm" [disabled]="rows().length < pageSize" (click)="go(page() + 1)">Next</button>
            </div>
          </div>
        }
      </section>

      @if (enrolling(); as admission) {
        <section class="card">
          <div class="card-header">
            <h3>Enrol {{ admission.firstName }} {{ admission.lastName }}</h3>
            <button class="btn btn-ghost btn-sm" (click)="enrolling.set(null)">Close</button>
          </div>
          <div class="card-body">
            <form class="form-grid" [formGroup]="enrolForm" (ngSubmit)="enrol(admission)">
              <label class="field">
                <span>Section *</span>
                <select formControlName="sectionId">
                  <option [ngValue]="null">Select section</option>
                  @for (section of sections(); track section.id) {
                    <option [ngValue]="section.id">{{ section.name }} ({{ section.studentCount }}/{{ section.capacity }})</option>
                  }
                </select>
              </label>
              <label class="field">
                <span>Admission number</span>
                <input formControlName="admissionNo" placeholder="Auto-generated when blank" />
              </label>
              <label class="field">
                <span>Roll number</span>
                <input formControlName="rollNo" placeholder="Auto-generated when blank" />
              </label>
              <label class="field">
                <span>Admission fee</span>
                <input type="number" formControlName="admissionFeeAmount" min="0" />
              </label>
              <label class="field full row" style="align-items:center">
                <input type="checkbox" formControlName="createParentLogin" style="width:auto;min-height:auto" />
                <span>Create a parent portal login and send credentials</span>
              </label>
              <div class="full row">
                <button class="btn btn-primary" type="submit" [disabled]="saving() || enrolForm.invalid">
                  {{ saving() ? 'Enrolling…' : 'Confirm enrolment' }}
                </button>
              </div>
            </form>
          </div>
        </section>
      }
    </div>
  `
})
export class AdmissionsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly rows = signal<Admission[]>([]);
  readonly classes = signal<ClassRoom[]>([]);
  readonly sessions = signal<AcademicSession[]>([]);
  readonly sections = signal<Section[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly formOpen = signal(false);
  readonly enrolling = signal<Admission | null>(null);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly search = signal('');
  readonly status = signal('');
  readonly pageSize = 20;

  readonly currentSession = computed(() => this.sessions().find(item => item.isCurrent) ?? this.sessions()[0]);
  readonly statuses = [
    'Enquiry', 'Application', 'DocumentsVerified', 'InterviewScheduled',
    'Approved', 'Rejected', 'Waitlisted', 'Enrolled', 'Cancelled'
  ];

  readonly form = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    dateOfBirth: ['', Validators.required],
    gender: ['Male', Validators.required],
    guardianName: ['', Validators.required],
    guardianPhone: ['', Validators.required],
    guardianEmail: [''],
    classId: this.fb.control<number | null>(null, Validators.required),
    previousSchool: [''],
    source: ['Walk-in'],
    notes: ['']
  });

  readonly enrolForm = this.fb.nonNullable.group({
    sectionId: this.fb.control<number | null>(null, Validators.required),
    admissionNo: [''],
    rollNo: [''],
    admissionFeeAmount: this.fb.control<number | null>(null),
    createParentLogin: [true]
  });

  ngOnInit(): void {
    this.loadLookups();
    this.load();
  }

  loadLookups(): void {
    this.api.get<ClassRoom[]>('admin/classes').subscribe({
      next: classes => this.classes.set(classes),
      error: () => this.error.set('Some information could not be loaded. Check your connection and refresh the page.')
    });
    this.api.get<AcademicSession[]>('admin/sessions').subscribe({
      next: sessions => this.sessions.set(sessions),
      error: () => this.error.set('Some information could not be loaded. Check your connection and refresh the page.')
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api
      .get<PagedResult<Admission>>('admissions', {
        page: this.page(),
        pageSize: this.pageSize,
        search: this.search(),
        status: this.status()
      })
      .subscribe({
        next: result => {
          this.rows.set(result.items);
          this.total.set(result.totalCount);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Could not load admissions.');
          this.loading.set(false);
        }
      });
  }

  setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  setStatus(event: Event): void {
    this.status.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  go(page: number): void {
    this.page.set(page);
    this.load();
  }

  create(): void {
    if (this.form.invalid || this.saving()) return;
    this.saving.set(true);
    this.error.set(null);

    const value = this.form.getRawValue();
    this.api
      .post<Admission>('admissions', {
        ...value,
        guardianEmail: value.guardianEmail?.trim() || null,
        classId: value.classId,
        sessionId: this.currentSession()?.id ?? null
      })
      .subscribe({
        next: created => {
          this.saving.set(false);
          this.formOpen.set(false);
          this.message.set(`Enquiry ${created.applicationNo} created.`);
          this.form.reset({
            firstName: '', lastName: '', dateOfBirth: '', gender: 'Male', guardianName: '',
            guardianPhone: '', guardianEmail: '', classId: null, previousSchool: '',
            source: 'Walk-in', notes: ''
          });
          this.load();
        },
        error: () => {
          this.saving.set(false);
          this.error.set('Could not create the enquiry. Check the required fields.');
        }
      });
  }

  action(row: Admission, action: string): void {
    this.error.set(null);
    this.api.post<Admission>(`admissions/${row.id}/${action}`).subscribe({
      next: updated => {
        this.message.set(`${updated.applicationNo} moved to ${updated.status}.`);
        this.load();
      },
      error: () => this.error.set(`Could not ${action.replace('-', ' ')} ${row.applicationNo}.`)
    });
  }

  reject(row: Admission): void {
    const reason = window.prompt(`Reason for rejecting ${row.applicationNo}?`);
    if (!reason) return;

    this.api.post<Admission>(`admissions/${row.id}/reject`, { reason }).subscribe({
      next: () => {
        this.message.set(`${row.applicationNo} rejected.`);
        this.load();
      },
      error: () => this.error.set('Could not reject the application.')
    });
  }

  startEnroll(row: Admission): void {
    this.enrolling.set(row);
    this.enrolForm.reset({ sectionId: null, admissionNo: '', rollNo: '', admissionFeeAmount: null, createParentLogin: true });
    this.sections.set([]);
    if (row.classId) {
      this.api.get<Section[]>(`admin/classes/${row.classId}/sections`).subscribe({
        next: sections => this.sections.set(sections),
        error: () => this.error.set('Could not load sections for this class.')
      });
    }
  }

  enrol(row: Admission): void {
    if (this.enrolForm.invalid || this.saving()) return;
    this.saving.set(true);
    const value = this.enrolForm.getRawValue();

    this.api
      .post<{ studentId: number; admissionNo: string; temporaryPassword?: string | null }>(
        `admissions/${row.id}/enroll`,
        {
          sectionId: value.sectionId,
          admissionNo: value.admissionNo || null,
          rollNo: value.rollNo || null,
          createParentLogin: value.createParentLogin,
          admissionFeeAmount: value.admissionFeeAmount
        }
      )
      .subscribe({
        next: result => {
          this.saving.set(false);
          this.enrolling.set(null);
          this.message.set(
            `${row.firstName} ${row.lastName} enrolled as ${result.admissionNo}.` +
            (result.temporaryPassword
              ? ` Parent login: ${result.admissionNo} / temporary password ${result.temporaryPassword}`
              : '')
          );
          this.load();
        },
        error: () => {
          this.saving.set(false);
          this.error.set('Enrolment failed. The section may be full, or a parent account may already exist.');
        }
      });
  }

  badge(status: string): string {
    if (status === 'Enrolled' || status === 'Approved' || status === 'DocumentsVerified') return 'green';
    if (status === 'Rejected' || status === 'Cancelled') return 'red';
    if (status === 'Waitlisted') return 'amber';
    return 'teal';
  }
}
