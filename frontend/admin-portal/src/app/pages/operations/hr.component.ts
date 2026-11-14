import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { localDate } from '../../core/dates';
import { readError } from '../platform/schools.component';

interface Employee {
  id: number; employeeNo: string; fullName: string; email: string; phone?: string | null;
  department?: string | null; designation?: string | null; joiningDate: string; status: string;
}
interface Lookup { id: number; name: string; }
interface Leave {
  id: number; employeeId: number; employeeName: string; leaveType: string; fromDate: string; toDate: string;
  reason: string; status: string; createdAtUtc: string;
}
interface PayrollRun {
  id: number; month: number; year: number; status: string; totalGross: number; totalDeductions: number;
  totalNet: number; employeeCount: number; processedAtUtc?: string | null;
}
interface Payslip {
  id: number; employeeName: string; basic: number; gross: number; deductions: number; net: number;
  daysPresent: number; daysAbsent: number;
}
type Tab = 'employees' | 'leave' | 'payroll';

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September',
  'October', 'November', 'December'];

/** Staff records, leave requests and monthly payroll. */
@Component({
  selector: 'app-hr',
  standalone: true,
  imports: [FormsModule, DatePipe, CurrencyPipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Operations</div>
          <h1>People &amp; HR</h1>
          <p class="subtitle">Employees, leave requests and monthly payroll.</p>
        </div>
      </header>

      @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
      @if (message()) { <div class="success" role="status">{{ message() }}</div> }

      <div class="tabs" role="tablist">
        <button class="tab" role="tab" [class.active]="tab() === 'employees'" (click)="tab.set('employees')">Employees</button>
        <button class="tab" role="tab" [class.active]="tab() === 'leave'" (click)="setTab('leave')">Leave</button>
        <button class="tab" role="tab" [class.active]="tab() === 'payroll'" (click)="setTab('payroll')">Payroll</button>
      </div>

      @if (tab() === 'employees') {
        @if (canManage()) {
          <section class="card">
            <div class="card-header"><h3>Add employee</h3></div>
            <div class="card-body">
              <div class="form-grid">
                <label class="field"><span>First name *</span><input [(ngModel)]="emp.firstName" /></label>
                <label class="field"><span>Last name *</span><input [(ngModel)]="emp.lastName" /></label>
                <label class="field"><span>Email *</span><input type="email" [(ngModel)]="emp.email" /></label>
                <label class="field"><span>Phone</span><input [(ngModel)]="emp.phone" /></label>
                <label class="field"><span>Department</span>
                  <select [(ngModel)]="emp.departmentId">
                    <option [ngValue]="null">—</option>
                    @for (d of departments(); track d.id) { <option [ngValue]="d.id">{{ d.name }}</option> }
                  </select>
                </label>
                <label class="field"><span>Designation</span>
                  <select [(ngModel)]="emp.designationId">
                    <option [ngValue]="null">—</option>
                    @for (d of designations(); track d.id) { <option [ngValue]="d.id">{{ d.name }}</option> }
                  </select>
                </label>
                <label class="field"><span>Joining date *</span><input type="date" [(ngModel)]="emp.joiningDate" /></label>
                <label class="field"><span>Basic salary (₹ / month)</span><input type="number" min="0" [(ngModel)]="emp.basicSalary" /></label>
                <label class="field"><span>Qualification</span><input [(ngModel)]="emp.qualification" /></label>
                <label class="field"><span>Experience (years)</span><input type="number" min="0" max="60" [(ngModel)]="emp.experienceYears" /></label>
                <div class="full"><button class="btn btn-primary" (click)="addEmployee()" [disabled]="saving() || !employeeValid()">
                  {{ saving() ? 'Saving…' : 'Add employee' }}</button></div>
              </div>
            </div>
          </section>
        }
        <section class="card">
          <div class="card-header">
            <h3>Employees ({{ employees().length }})</h3>
            <input [(ngModel)]="search" (keyup.enter)="loadEmployees()" aria-label="Search employees"
                   placeholder="Search name or email" style="width:100%;max-width:240px" />
          </div>
          @if (loading()) { <div class="loading">Loading…</div> }
          @else if (!employees().length) { <div class="empty">No employees found.</div> }
          @else {
            <div class="table-wrap"><table>
              <thead><tr><th>Emp. no.</th><th>Name</th><th>Email</th><th>Department</th><th>Designation</th><th>Joined</th><th>Status</th></tr></thead>
              <tbody>
                @for (e of employees(); track e.id) {
                  <tr>
                    <td>{{ e.employeeNo }}</td><td class="primary">{{ e.fullName }}</td><td>{{ e.email }}</td>
                    <td>{{ e.department || '—' }}</td><td>{{ e.designation || '—' }}</td>
                    <td>{{ e.joiningDate | date:'dd MMM yyyy' }}</td>
                    <td><span class="badge" [class.teal]="e.status === 'Active'">{{ e.status }}</span></td>
                  </tr>
                }
              </tbody>
            </table></div>
          }
        </section>
      }

      @if (tab() === 'leave') {
        <section class="card">
          <div class="card-header"><h3>Apply for leave</h3></div>
          <div class="card-body">
            <div class="form-grid">
              @if (canManage()) {
                <label class="field"><span>Employee *</span>
                  <select [(ngModel)]="leave.employeeId">
                    <option [ngValue]="null">Myself</option>
                    @for (e of employees(); track e.id) { <option [ngValue]="e.id">{{ e.fullName }} ({{ e.employeeNo }})</option> }
                  </select>
                </label>
              }
              <label class="field"><span>Type *</span>
                <select [(ngModel)]="leave.leaveType">
                  @for (t of leaveTypes; track t) { <option [value]="t">{{ t }}</option> }
                </select>
              </label>
              <label class="field"><span>From *</span><input type="date" [(ngModel)]="leave.fromDate" /></label>
              <label class="field"><span>To *</span><input type="date" [(ngModel)]="leave.toDate" /></label>
              <label class="field full"><span>Reason *</span><input [(ngModel)]="leave.reason" /></label>
              <div class="full"><button class="btn btn-primary" (click)="applyLeave()" [disabled]="saving() || !leaveValid()">Submit request</button>
                @if (leave.fromDate && leave.toDate && leave.toDate < leave.fromDate) { <span class="small" style="color:var(--red-500)"> "To" must be on or after "From".</span> }
              </div>
            </div>
          </div>
        </section>
        <section class="card">
          <div class="card-header">
            <h3>Leave requests</h3>
            <select [(ngModel)]="leaveFilter" (ngModelChange)="loadLeaves()" aria-label="Filter leave by status">
              <option value="">All</option><option value="Pending">Pending</option>
              <option value="Approved">Approved</option><option value="Rejected">Rejected</option>
            </select>
          </div>
          @if (!leaves().length) { <div class="empty">No leave requests.</div> }
          @else {
            <div class="table-wrap"><table>
              <thead><tr><th>Employee</th><th>Type</th><th>From</th><th>To</th><th>Reason</th><th>Status</th><th></th></tr></thead>
              <tbody>
                @for (l of leaves(); track l.id) {
                  <tr>
                    <td class="primary">{{ l.employeeName }}</td><td>{{ l.leaveType }}</td>
                    <td>{{ l.fromDate | date:'dd MMM yyyy' }}</td><td>{{ l.toDate | date:'dd MMM yyyy' }}</td>
                    <td>{{ l.reason }}</td>
                    <td><span class="badge" [class.teal]="l.status === 'Approved'">{{ l.status }}</span></td>
                    <td>
                      @if (canManage() && l.status === 'Pending') {
                        <div class="row" style="gap:6px;justify-content:flex-end">
                          <button class="btn btn-primary btn-sm" [disabled]="saving()" (click)="decide(l, true)">Approve</button>
                          <button class="btn btn-ghost btn-sm" [disabled]="saving()" (click)="decide(l, false)">Reject</button>
                        </div>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table></div>
          }
        </section>
      }

      @if (tab() === 'payroll') {
        @if (canManage()) {
          <section class="card">
            <div class="card-header"><h3>Run payroll</h3></div>
            <div class="card-body">
              <div class="row" style="gap:8px;flex-wrap:wrap">
                <select [(ngModel)]="payMonth" aria-label="Month">
                  @for (m of months; track $index) { <option [ngValue]="$index + 1">{{ m }}</option> }
                </select>
                <input type="number" [(ngModel)]="payYear" aria-label="Year" min="2000" max="2100" style="width:100px" />
                <button class="btn btn-primary btn-sm" (click)="runPayroll()" [disabled]="saving()">
                  {{ saving() ? 'Processing…' : 'Process payroll' }}</button>
              </div>
              <p class="small muted">Uses each employee's basic salary and staff attendance for the month.</p>
            </div>
          </section>
        }
        <section class="card">
          <div class="card-header"><h3>Payroll runs</h3></div>
          @if (!runs().length) { <div class="empty">No payroll has been processed yet.</div> }
          @else {
            <div class="table-wrap"><table>
              <thead><tr><th>Month</th><th>Employees</th><th>Gross</th><th>Deductions</th><th>Net</th><th>Status</th><th></th></tr></thead>
              <tbody>
                @for (r of runs(); track r.id) {
                  <tr>
                    <td class="primary">{{ months[r.month - 1] }} {{ r.year }}</td><td>{{ r.employeeCount }}</td>
                    <td>{{ r.totalGross | currency:'INR':'symbol':'1.0-0' }}</td>
                    <td>{{ r.totalDeductions | currency:'INR':'symbol':'1.0-0' }}</td>
                    <td><strong>{{ r.totalNet | currency:'INR':'symbol':'1.0-0' }}</strong></td>
                    <td><span class="badge teal">{{ r.status }}</span></td>
                    <td><button class="btn btn-ghost btn-sm" (click)="showPayslips(r)">{{ openRun() === r.id ? 'Hide' : 'Payslips' }}</button></td>
                  </tr>
                  @if (openRun() === r.id) {
                    <tr><td colspan="7">
                      @if (!payslips().length) { <div class="small muted">No payslips.</div> }
                      @else {
                        <table>
                          <thead><tr><th>Employee</th><th>Present</th><th>Absent</th><th>Basic</th><th>Gross</th><th>Deductions</th><th>Net</th></tr></thead>
                          <tbody>
                            @for (p of payslips(); track p.id) {
                              <tr><td>{{ p.employeeName }}</td><td>{{ p.daysPresent }}</td><td>{{ p.daysAbsent }}</td>
                                <td>{{ p.basic | currency:'INR':'symbol':'1.0-0' }}</td><td>{{ p.gross | currency:'INR':'symbol':'1.0-0' }}</td>
                                <td>{{ p.deductions | currency:'INR':'symbol':'1.0-0' }}</td><td><strong>{{ p.net | currency:'INR':'symbol':'1.0-0' }}</strong></td></tr>
                            }
                          </tbody>
                        </table>
                      }
                    </td></tr>
                  }
                }
              </tbody>
            </table></div>
          }
        </section>
      }
    </div>
  `
})
export class HrComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  readonly canManage = computed(() => this.auth.has('hr.manage'));
  readonly tab = signal<Tab>('employees');
  readonly employees = signal<Employee[]>([]);
  readonly departments = signal<Lookup[]>([]);
  readonly designations = signal<Lookup[]>([]);
  readonly leaves = signal<Leave[]>([]);
  readonly runs = signal<PayrollRun[]>([]);
  readonly payslips = signal<Payslip[]>([]);
  readonly openRun = signal<number | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  readonly months = MONTHS;
  readonly leaveTypes = ['Casual', 'Sick', 'Earned', 'Maternity', 'Paternity', 'Unpaid'];

  search = '';
  leaveFilter = '';
  payMonth = new Date().getMonth() + 1;
  payYear = new Date().getFullYear();
  emp = this.blankEmployee();
  leave = { employeeId: null as number | null, leaveType: 'Casual', fromDate: localDate(), toDate: localDate(), reason: '' };

  ngOnInit(): void {
    this.loadEmployees();
    this.api.get<Lookup[]>('hr/departments').subscribe({ next: d => this.departments.set(d), error: r => this.fail(r) });
    this.api.get<Lookup[]>('hr/designations').subscribe({ next: d => this.designations.set(d), error: r => this.fail(r) });
  }

  setTab(tab: Tab): void {
    this.tab.set(tab);
    if (tab === 'leave') this.loadLeaves();
    if (tab === 'payroll') this.loadRuns();
  }

  loadEmployees(): void {
    this.loading.set(true);
    this.api.get<Employee[]>('hr/employees', { search: this.search.trim() || null }).subscribe({
      next: e => { this.employees.set(e); this.loading.set(false); },
      error: r => { this.loading.set(false); this.fail(r); }
    });
  }

  loadLeaves(): void {
    this.api.get<Leave[]>('hr/leaves', { status: this.leaveFilter || null }).subscribe({
      next: l => this.leaves.set(l), error: r => this.fail(r)
    });
  }

  loadRuns(): void {
    this.api.get<PayrollRun[]>('hr/payroll').subscribe({ next: r => this.runs.set(r), error: r => this.fail(r) });
  }

  employeeValid(): boolean {
    const e = this.emp;
    return !!e.firstName.trim() && !!e.lastName.trim() && /^\S+@\S+\.\S+$/.test(e.email.trim()) && !!e.joiningDate &&
      (e.basicSalary === null || Number(e.basicSalary) >= 0);
  }

  addEmployee(): void {
    if (this.saving() || !this.employeeValid()) return;
    this.saving.set(true);
    const e = this.emp;
    this.api.post('hr/employees', {
      firstName: e.firstName.trim(), lastName: e.lastName.trim(), email: e.email.trim(), phone: e.phone.trim() || null,
      departmentId: e.departmentId, designationId: e.designationId, joiningDate: e.joiningDate,
      qualification: e.qualification.trim() || null,
      experienceYears: e.experienceYears === null ? null : Number(e.experienceYears),
      basicSalary: e.basicSalary === null ? null : Number(e.basicSalary)
    }).subscribe({
      next: () => { this.saving.set(false); this.ok(`${e.firstName} ${e.lastName} added.`); this.emp = this.blankEmployee(); this.loadEmployees(); },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  leaveValid(): boolean {
    const l = this.leave;
    return !!l.reason.trim() && !!l.fromDate && !!l.toDate && l.toDate >= l.fromDate;
  }

  applyLeave(): void {
    if (this.saving() || !this.leaveValid()) return;
    this.saving.set(true);
    this.api.post('hr/leaves', { ...this.leave, reason: this.leave.reason.trim() }).subscribe({
      next: () => { this.saving.set(false); this.ok('Leave request submitted.'); this.leave.reason = ''; this.loadLeaves(); },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  decide(l: Leave, approve: boolean): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.api.post(`hr/leaves/${l.id}/approve?approve=${approve}`).subscribe({
      next: () => { this.saving.set(false); this.ok(`Leave ${approve ? 'approved' : 'rejected'} for ${l.employeeName}.`); this.loadLeaves(); },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  runPayroll(): void {
    if (this.saving()) return;
    if (!confirm(`Process payroll for ${MONTHS[this.payMonth - 1]} ${this.payYear}?`)) return;
    this.saving.set(true);
    this.api.post('hr/payroll', { month: Number(this.payMonth), year: Number(this.payYear) }).subscribe({
      next: () => { this.saving.set(false); this.ok(`Payroll processed for ${MONTHS[this.payMonth - 1]} ${this.payYear}.`); this.loadRuns(); },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  showPayslips(r: PayrollRun): void {
    if (this.openRun() === r.id) { this.openRun.set(null); return; }
    this.openRun.set(r.id);
    this.payslips.set([]);
    this.api.get<Payslip[]>(`hr/payroll/${r.id}/payslips`).subscribe({ next: p => this.payslips.set(p), error: e => this.fail(e) });
  }

  private blankEmployee() {
    return {
      firstName: '', lastName: '', email: '', phone: '', departmentId: null as number | null,
      designationId: null as number | null, joiningDate: localDate(), basicSalary: null as number | null,
      qualification: '', experienceYears: null as number | null
    };
  }

  private ok(text: string): void { this.error.set(null); this.message.set(text); }
  private fail(r: unknown): void { this.message.set(null); this.error.set(readError(r)); }
}
