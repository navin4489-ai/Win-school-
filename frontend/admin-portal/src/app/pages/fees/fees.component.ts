import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import {
  ClassRoom, CollectionSummary, Defaulter, Ledger, PagedResult, Payment, PaymentMode
} from '../../core/models';

type Tab = 'collect' | 'outstanding' | 'payments';

@Component({
  selector: 'app-fees',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, CurrencyPipe, DatePipe, DecimalPipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Finance</div>
          <h1>Fees &amp; payments</h1>
          <p class="subtitle">Collect fees, track outstanding balances and review receipts.</p>
        </div>
        <div class="toolbar">
          <span class="badge teal">Append-only ledger</span>
          <button class="btn btn-ghost btn-sm" (click)="queueReminders()">Queue reminders</button>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      @if (summary(); as s) {
        <section class="stat-grid">
          <article class="stat tone-positive">
            <div class="label">Collected this month</div>
            <div class="value">{{ s.totalCollected | currency:'INR':'symbol':'1.0-0' }}</div>
            <div class="hint">{{ s.paymentCount }} payments</div>
          </article>
          <article class="stat tone-warning">
            <div class="label">Refunded</div>
            <div class="value">{{ s.totalRefunded | currency:'INR':'symbol':'1.0-0' }}</div>
            <div class="hint">Approved refunds</div>
          </article>
          <article class="stat tone-info">
            <div class="label">Outstanding students</div>
            <div class="value">{{ outstanding().length }}</div>
            <div class="hint">Across active classes</div>
          </article>
          <article class="stat tone-danger">
            <div class="label">Overdue exposure</div>
            <div class="value">{{ overdueTotal() | currency:'INR':'symbol':'1.0-0' }}</div>
            <div class="hint">Oldest due dates first</div>
          </article>
        </section>
      }

      <div class="tabs">
        <button class="tab" [class.active]="tab() === 'collect'" (click)="setTab('collect')">Collect</button>
        <button class="tab" [class.active]="tab() === 'outstanding'" (click)="setTab('outstanding')">Outstanding</button>
        <button class="tab" [class.active]="tab() === 'payments'" (click)="setTab('payments')">Recent payments</button>
      </div>

      @if (tab() === 'collect') {
        <section class="split">
          <article class="card">
            <div class="card-header">
              <h3>Record a payment</h3>
              @if (ledger(); as l) { <span class="badge">{{ l.studentName }}</span> }
            </div>
            <div class="card-body">
              <form [formGroup]="form" (ngSubmit)="pay()" class="form-grid">
                <label class="field">
                  <span>Student ID *</span>
                  <div class="row" style="flex-wrap:nowrap">
                    <input type="number" min="1" formControlName="studentId" />
                    <button class="btn btn-ghost btn-sm" type="button" (click)="loadLedger()">Load ledger</button>
                  </div>
                </label>
                <label class="field">
                  <span>Amount *</span>
                  <input type="number" min="1" step="0.01" formControlName="amount" />
                </label>
                <label class="field">
                  <span>Mode *</span>
                  <select formControlName="mode">
                    @for (mode of modes; track mode) { <option [value]="mode">{{ mode }}</option> }
                  </select>
                </label>
                <label class="field">
                  <span>Reference no.</span>
                  <input formControlName="referenceNo" placeholder="UPI / cheque / gateway reference" />
                </label>
                <label class="field full">
                  <span>Remarks</span>
                  <input formControlName="remarks" placeholder="Optional note for the receipt" />
                </label>
                <div class="full row">
                  <button class="btn btn-primary" type="submit" [disabled]="saving() || form.invalid">
                    {{ saving() ? 'Recording…' : 'Record payment' }}
                  </button>
                  <span class="small muted">A receipt number is generated automatically.</span>
                </div>
              </form>
            </div>
          </article>

          <article class="card">
            <div class="card-header"><h3>Student ledger</h3><a routerLink="/students" class="small">Find student →</a></div>
            <div class="card-body">
              @if (ledger(); as l) {
                <div class="row" style="justify-content:space-between">
                  <div>
                    <div style="font-weight:650">{{ l.studentName }}</div>
                    <div class="small muted">{{ l.admissionNo }} · {{ l.className }}</div>
                  </div>
                  <a class="btn btn-ghost btn-sm" [routerLink]="['/students', l.studentId]">Profile</a>
                </div>
                <div class="divider"></div>
                <div class="stat-grid" style="grid-template-columns:repeat(2,1fr)">
                  <div><div class="small muted">Billed</div><strong>{{ l.totalBilled | currency:'INR':'symbol':'1.0-0' }}</strong></div>
                  <div><div class="small muted">Paid</div><strong>{{ l.totalPaid | currency:'INR':'symbol':'1.0-0' }}</strong></div>
                  <div><div class="small muted">Balance</div><strong>{{ l.balance | currency:'INR':'symbol':'1.0-0' }}</strong></div>
                  <div><div class="small muted">Overdue</div><strong style="color:var(--red-500)">{{ l.overdue | currency:'INR':'symbol':'1.0-0' }}</strong></div>
                </div>
                <div class="divider"></div>
                <div class="list">
                  @for (installment of l.installments; track installment.id) {
                    <div class="list-item">
                      <div [class]="'dot ' + (installment.isOverdue ? 'red' : installment.balance <= 0 ? 'green' : '')"></div>
                      <div style="flex:1">
                        <div style="font-weight:600">{{ installment.name }}</div>
                        <div class="small muted">Due {{ installment.dueDate | date:'dd MMM yyyy' }} · {{ installment.status }}</div>
                      </div>
                      <div style="text-align:right">
                        <div>{{ installment.balance | currency:'INR':'symbol':'1.0-0' }}</div>
                        @if (installment.balance > 0) {
                          <button class="btn btn-ghost btn-sm" type="button" (click)="payInstallment(installment.id, installment.balance)">Pay</button>
                        }
                      </div>
                    </div>
                  }
                </div>
              } @else {
                <div class="empty">Enter a student ID and load the ledger to see installments.</div>
              }
            </div>
          </article>
        </section>
      }

      @if (tab() === 'outstanding') {
        <section class="card">
          <div class="card-header">
            <h3>Outstanding balances</h3>
            <div class="row">
              <select [value]="classId()" (change)="setClass($event)" style="min-height:34px;border:1px solid var(--line);border-radius:8px;padding:4px 8px">
                <option value="">All classes</option>
                @for (item of classes(); track item.id) { <option [value]="item.id">{{ item.name }}</option> }
              </select>
              <button class="btn btn-ghost btn-sm" (click)="loadOutstanding()">Refresh</button>
            </div>
          </div>
          @if (loading()) {
            <div class="loading">Loading outstanding balances…</div>
          } @else if (!outstanding().length) {
            <div class="empty">No outstanding balances. Every ledger is settled.</div>
          } @else {
            <div class="table-wrap">
              <table>
                <thead><tr><th>Student</th><th>Class</th><th>Guardian phone</th><th>Oldest due</th><th>Outstanding</th><th></th></tr></thead>
                <tbody>
                  @for (row of outstanding(); track row.studentId) {
                    <tr>
                      <td>
                        <div class="primary">{{ row.studentName }}</div>
                        <div class="secondary">{{ row.admissionNo }}</div>
                      </td>
                      <td>{{ row.className }}</td>
                      <td>{{ row.guardianPhone || '—' }}</td>
                      <td>{{ row.oldestDueDate | date:'dd MMM yyyy' }}</td>
                      <td><strong>{{ row.outstanding | currency:'INR':'symbol':'1.0-0' }}</strong></td>
                      <td><a class="btn btn-ghost btn-sm" [routerLink]="['/students', row.studentId]">Open</a></td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      }

      @if (tab() === 'payments') {
        <section class="card">
          <div class="card-header">
            <h3>Recent payments</h3>
            <a routerLink="/reports" class="small">Export report →</a>
          </div>
          @if (loading()) {
            <div class="loading">Loading payments…</div>
          } @else if (!payments().length) {
            <div class="empty">No payments recorded this month.</div>
          } @else {
            <div class="table-wrap">
              <table>
                <thead><tr><th>Receipt</th><th>Student</th><th>Mode</th><th>Paid</th><th>Amount</th><th>Status</th></tr></thead>
                <tbody>
                  @for (payment of payments(); track payment.id) {
                    <tr>
                      <td class="primary">{{ payment.receiptNo }}</td>
                      <td>{{ payment.studentName }}<div class="secondary">#{{ payment.studentId }}</div></td>
                      <td>{{ payment.mode }}</td>
                      <td class="small muted">{{ payment.paidAtUtc | date:'dd MMM, HH:mm' }}</td>
                      <td>{{ payment.amount | currency:'INR':'symbol':'1.0-0' }}</td>
                      <td><span class="badge green">{{ payment.status }}</span></td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      }
    </div>
  `
})
export class FeesComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly tab = signal<Tab>('collect');
  readonly ledger = signal<Ledger | null>(null);
  readonly outstanding = signal<Defaulter[]>([]);
  readonly payments = signal<Payment[]>([]);
  readonly classes = signal<ClassRoom[]>([]);
  readonly summary = signal<CollectionSummary | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly classId = signal('');

  readonly modes: PaymentMode[] = ['Cash', 'Upi', 'Card', 'NetBanking', 'Cheque', 'BankTransfer', 'Online'];

  readonly form = this.fb.nonNullable.group({
    studentId: [0, [Validators.required, Validators.min(1)]],
    amount: [0, [Validators.required, Validators.min(1)]],
    mode: ['Cash' as PaymentMode, Validators.required],
    referenceNo: [''],
    remarks: ['']
  });

  private feeInstallmentId: number | null = null;

  ngOnInit(): void {
    const now = new Date();
    const from = new Date(now.getFullYear(), now.getMonth(), 1);
    this.api.get<ClassRoom[]>('admin/classes').subscribe({ next: rows => this.classes.set(rows), error: () => undefined });
    this.api
      .get<CollectionSummary>('fees/summary', { from: iso(from), to: iso(now) })
      .subscribe({ next: value => this.summary.set(value), error: () => undefined });
    this.loadPayments(from, now);
    this.loadOutstanding();
  }

  setTab(tab: Tab): void {
    this.tab.set(tab);
    if (tab === 'outstanding') this.loadOutstanding();
    if (tab === 'payments') {
      const now = new Date();
      this.loadPayments(new Date(now.getFullYear(), now.getMonth(), 1), now);
    }
  }

  setClass(event: Event): void {
    this.classId.set((event.target as HTMLSelectElement).value);
    this.loadOutstanding();
  }

  loadLedger(): void {
    const studentId = this.form.controls.studentId.value;
    if (!studentId) return;
    this.error.set(null);
    this.api.get<Ledger>(`fees/students/${studentId}/ledger`).subscribe({
      next: ledger => {
        this.ledger.set(ledger);
        this.form.patchValue({ studentId: ledger.studentId });
      },
      error: () => {
        this.ledger.set(null);
        this.error.set(`No fee ledger found for student #${studentId}.`);
      }
    });
  }

  payInstallment(installmentId: number, balance: number): void {
    this.feeInstallmentId = installmentId;
    this.form.patchValue({ amount: balance });
  }

  pay(): void {
    if (this.form.invalid || this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.message.set(null);

    const value = this.form.getRawValue();
    this.api
      .post<Payment>('fees/payments', {
        studentId: value.studentId,
        feeInstallmentId: this.feeInstallmentId,
        amount: value.amount,
        mode: value.mode,
        referenceNo: value.referenceNo || null,
        idempotencyKey: crypto.randomUUID(),
        remarks: value.remarks || null
      })
      .subscribe({
        next: payment => {
          this.saving.set(false);
          this.message.set(`Receipt ${payment.receiptNo} recorded for ${payment.amount.toLocaleString('en-IN')}.`);
          this.feeInstallmentId = null;
          this.form.patchValue({ amount: 0, referenceNo: '', remarks: '' });
          this.loadLedger();
          this.loadOutstanding();
          const now = new Date();
          this.loadPayments(new Date(now.getFullYear(), now.getMonth(), 1), now);
        },
        error: () => {
          this.saving.set(false);
          this.error.set('Payment could not be recorded. Check the amount and installment.');
        }
      });
  }

  queueReminders(): void {
    this.api.post<{ queued: number; skipped: number }>('fees/reminders').subscribe({
      next: result => this.message.set(`${result.queued} reminders queued, ${result.skipped} skipped.`),
      error: () => this.error.set('Could not queue reminders.')
    });
  }

  loadOutstanding(): void {
    this.loading.set(true);
    this.api
      .get<Defaulter[]>('fees/outstanding', { classId: this.classId() })
      .subscribe({
        next: rows => {
          this.outstanding.set(rows);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Could not load outstanding balances.');
          this.loading.set(false);
        }
      });
  }

  private loadPayments(from: Date, to: Date): void {
    this.api
      .get<PagedResult<Payment>>('fees/payments', { page: 1, pageSize: 25, from: iso(from), to: iso(to) })
      .subscribe({ next: result => this.payments.set(result.items), error: () => undefined });
  }

  overdueTotal(): number {
    return this.outstanding().reduce((sum, row) => sum + row.outstanding, 0);
  }
}

function iso(date: Date): string {
  return date.toISOString().slice(0, 10);
}
