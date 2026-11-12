import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AttendanceSummary, Ledger, StudentDetail } from '../../core/models';

@Component({
  selector: 'app-student-detail',
  standalone: true,
  imports: [DatePipe, DecimalPipe, CurrencyPipe, RouterLink, FormsModule],
  template: `
    <div class="page">
      <div class="row">
        <a routerLink="/students" class="small">← Back to students</a>
      </div>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (loading()) {
        <div class="loading">Loading student profile…</div>
      } @else if (student(); as s) {
        <header class="page-header">
          <div class="row" style="gap:14px">
            <div class="avatar" style="width:56px;height:56px;font-size:19px">{{ initials(s.fullName) }}</div>
            <div>
              <div class="eyebrow">Student profile</div>
              <h1>{{ s.fullName }}</h1>
              <p class="subtitle">{{ s.admissionNo }} · {{ s.className }} – {{ s.sectionName }} · Roll {{ s.rollNo || '—' }}</p>
            </div>
          </div>
          <div class="toolbar">
            <span [class]="'badge ' + (s.status === 'Active' ? 'green' : 'amber')">{{ s.status }}</span>
            <button class="btn btn-ghost btn-sm" (click)="downloadCertificate('bonafide')">Bonafide</button>
            <button class="btn btn-ghost btn-sm" (click)="downloadCertificate('character')">Character</button>
            <button class="btn btn-ghost btn-sm" (click)="downloadCertificate('transfer')">Transfer</button>
          </div>
        </header>

        <section class="split">
          <div style="display:grid;gap:20px">
            <article class="card">
              <div class="card-header"><h3>Personal details</h3><span class="small muted">Admitted {{ s.admissionDate | date:'dd MMM yyyy' }}</span></div>
              <div class="card-body">
                <div class="form-grid">
                  <div><div class="small muted">Date of birth</div><div>{{ s.dateOfBirth | date:'dd MMM yyyy' }}</div></div>
                  <div><div class="small muted">Gender</div><div>{{ s.gender }}</div></div>
                  <div><div class="small muted">Blood group</div><div>{{ s.bloodGroup || '—' }}</div></div>
                  <div><div class="small muted">Previous school</div><div>{{ s.previousSchool || '—' }}</div></div>
                  <div><div class="small muted">Email</div><div>{{ s.email || '—' }}</div></div>
                  <div><div class="small muted">Phone</div><div>{{ s.phone || '—' }}</div></div>
                  <div class="full"><div class="small muted">Address</div><div>{{ address(s) }}</div></div>
                </div>
              </div>
            </article>

            <article class="card">
              <div class="card-header"><h3>Guardians</h3><span class="badge">{{ s.guardians.length }}</span></div>
              <div class="card-body">
                @if (s.guardians.length) {
                  <div class="list">
                    @for (guardian of s.guardians; track guardian.id) {
                      <div class="list-item">
                        <div class="avatar">{{ initials(guardian.fullName) }}</div>
                        <div style="flex:1">
                          <div style="font-weight:600">{{ guardian.fullName }} <span class="small muted">({{ guardian.relation }})</span></div>
                          <div class="small muted">{{ guardian.phone }}{{ guardian.email ? ' · ' + guardian.email : '' }}</div>
                        </div>
                        @if (guardian.isPrimary) { <span class="badge teal">Primary</span> }
                      </div>
                    }
                  </div>
                } @else {
                  <div class="empty">No guardians linked.</div>
                }
              </div>
            </article>

            <article class="card">
              <div class="card-header">
                <h3>Documents</h3>
                <div class="row">
                  <select [(ngModel)]="documentType" style="min-height:32px;border:1px solid var(--line);border-radius:8px;padding:4px 8px">
                    <option value="BirthCertificate">Birth certificate</option>
                    <option value="AddressProof">Address proof</option>
                    <option value="Photograph">Photograph</option>
                    <option value="TransferCertificate">Transfer certificate</option>
                    <option value="Marksheet">Marksheet</option>
                    <option value="IdProof">ID proof</option>
                    <option value="MedicalRecord">Medical record</option>
                    <option value="Other">Other</option>
                  </select>
                  <label class="btn btn-ghost btn-sm" style="margin:0">
                    Upload
                    <input type="file" hidden (change)="upload($event)" />
                  </label>
                </div>
              </div>
              <div class="card-body">
                @if (s.documents.length) {
                  <div class="table-wrap">
                    <table style="min-width:0">
                      <thead><tr><th>File</th><th>Type</th><th>Size</th><th></th></tr></thead>
                      <tbody>
                        @for (document of s.documents; track document.id) {
                          <tr>
                            <td>{{ document.fileName }}</td>
                            <td><span class="badge">{{ document.documentType }}</span></td>
                            <td class="small muted">{{ document.sizeBytes / 1024 | number:'1.0-0' }} KB</td>
                            <td>
                              @if (document.url) { <a [href]="document.url" target="_blank" rel="noopener">View</a> }
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                } @else {
                  <div class="empty">No documents uploaded yet.</div>
                }
              </div>
            </article>
          </div>

          <div style="display:grid;gap:20px">
            <article class="card">
              <div class="card-header"><h3>Fee ledger</h3><a routerLink="/fees" class="small">Collect →</a></div>
              <div class="card-body">
                @if (ledger(); as l) {
                  <div class="stat-grid" style="grid-template-columns:1fr 1fr">
                    <div class="stat"><div class="label">Billed</div><div class="value" style="font-size:19px">{{ l.totalBilled | currency:'INR':'symbol':'1.0-0' }}</div></div>
                    <div class="stat"><div class="label">Paid</div><div class="value" style="font-size:19px">{{ l.totalPaid | currency:'INR':'symbol':'1.0-0' }}</div></div>
                    <div class="stat"><div class="label">Balance</div><div class="value" style="font-size:19px">{{ l.balance | currency:'INR':'symbol':'1.0-0' }}</div></div>
                    <div class="stat"><div class="label">Overdue</div><div class="value" style="font-size:19px">{{ l.overdue | currency:'INR':'symbol':'1.0-0' }}</div></div>
                  </div>
                  <div class="list" style="margin-top:16px">
                    @for (installment of l.installments; track installment.id) {
                      <div class="list-item">
                        <div [class]="'dot ' + (installment.isOverdue ? 'red' : '')"></div>
                        <div style="flex:1">
                          <div style="font-weight:600">{{ installment.name }}</div>
                          <div class="small muted">Due {{ installment.dueDate | date:'dd MMM yyyy' }}</div>
                        </div>
                        <div style="text-align:right">
                          <div>{{ installment.amount | currency:'INR':'symbol':'1.0-0' }}</div>
                          <div class="small muted">{{ installment.status }}</div>
                        </div>
                      </div>
                    }
                  </div>
                } @else {
                  <div class="empty">No fee ledger available.</div>
                }
              </div>
            </article>

            <article class="card">
              <div class="card-header"><h3>Attendance</h3><span class="small muted">Last 30 days</span></div>
              <div class="card-body">
                @if (attendance(); as a) {
                  <div class="row" style="justify-content:space-between">
                    <div class="stat" style="flex:1"><div class="label">Present</div><div class="value">{{ a.present }}</div></div>
                    <div class="stat" style="flex:1"><div class="label">Absent</div><div class="value">{{ a.absent }}</div></div>
                    <div class="stat" style="flex:1"><div class="label">Rate</div><div class="value">{{ a.percentage | number:'1.0-0' }}<span class="unit">%</span></div></div>
                  </div>
                  <div class="divider"></div>
                  <div class="list">
                    @for (record of a.records.slice(0, 8); track record.date) {
                      <div class="list-item">
                        <div [class]="'dot ' + (record.status === 'Present' ? 'green' : record.status === 'Absent' ? 'red' : 'amber')"></div>
                        <div style="flex:1">{{ record.date | date:'dd MMM yyyy' }}</div>
                        <span class="badge">{{ record.status }}</span>
                      </div>
                    }
                  </div>
                } @else {
                  <div class="empty">No attendance records.</div>
                }
              </div>
            </article>

            <article class="card">
              <div class="card-header"><h3>Enrolment history</h3></div>
              <div class="card-body">
                <div class="list">
                  @for (enrolment of s.enrollments; track enrolment.id) {
                    <div class="list-item">
                      <div class="dot"></div>
                      <div style="flex:1">
                        <div style="font-weight:600">{{ enrolment.className }} – {{ enrolment.sectionName }}</div>
                        <div class="small muted">{{ enrolment.sessionName }} · {{ enrolment.startDate | date:'MMM yyyy' }} – {{ enrolment.endDate ? (enrolment.endDate | date:'MMM yyyy') : 'present' }}</div>
                      </div>
                      <span class="badge">{{ enrolment.status }}</span>
                    </div>
                  }
                </div>
              </div>
            </article>
          </div>
        </section>
      }
    </div>
  `
})
export class StudentDetailComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  readonly student = signal<StudentDetail | null>(null);
  readonly ledger = signal<Ledger | null>(null);
  readonly attendance = signal<AttendanceSummary | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  documentType = 'BirthCertificate';

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!id) {
      this.error.set('Invalid student.');
      this.loading.set(false);
      return;
    }
    this.load(id);
  }

  load(id: number): void {
    this.loading.set(true);
    this.api.get<StudentDetail>(`students/${id}`).subscribe({
      next: student => {
        this.student.set(student);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load this student.');
        this.loading.set(false);
      }
    });

    this.api.get<Ledger>(`fees/students/${id}/ledger`).subscribe({
      next: ledger => this.ledger.set(ledger),
      error: () => undefined
    });

    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - 30);
    this.api
      .get<AttendanceSummary>(`attendance/students/${id}`, { from: iso(from), to: iso(to) })
      .subscribe({ next: summary => this.attendance.set(summary), error: () => undefined });
  }

  upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const student = this.student();
    if (!file || !student) return;

    const body = new FormData();
    body.append('file', file);
    body.append('documentType', this.documentType);

    this.api.upload(`students/${student.id}/documents`, body).subscribe({
      next: () => {
        input.value = '';
        this.load(student.id);
      },
      error: () => this.error.set('Upload failed. Files must be 10 MB or smaller.')
    });
  }

  downloadCertificate(type: string): void {
    const student = this.student();
    if (!student) return;

    this.api.downloadPost(`students/${student.id}/certificates/${type}`).subscribe({
      next: response => this.api.saveBlob(response, `${type}-${student.admissionNo}.html`),
      error: () => this.error.set('Could not generate the certificate.')
    });
  }

  initials(name: string): string {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]?.toUpperCase() ?? '').join('');
  }

  address(student: StudentDetail): string {
    return [student.address, student.city, student.state, student.postalCode].filter(Boolean).join(', ') || '—';
  }
}

function iso(date: Date): string {
  return date.toISOString().slice(0, 10);
}
