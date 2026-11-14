import { localDate } from '../../core/dates';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import {
  Child, ClassRoom, CollectionSummary, Defaulter, Installment, Ledger, PagedResult, Payment, PaymentMode, PaymentOrder
} from '../../core/models';

type Tab = 'collect' | 'outstanding' | 'payments' | 'my-fees';
type LedgerAction = 'discount' | 'cancel' | 'edit' | 'waive';

@Component({
  selector: 'app-fees',
  standalone: true,
  imports: [ReactiveFormsModule, FormsModule, RouterLink, CurrencyPipe, DatePipe, DecimalPipe],
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
        @if (isParent()) {
          <button class="tab" [class.active]="tab() === 'my-fees'" (click)="setTab('my-fees')">{{ isStudent() ? 'My fees' : 'My children' }}</button>
          <button class="tab" [class.active]="tab() === 'payments'" (click)="setTab('payments')">Payment history</button>
        } @else {
          <button class="tab" [class.active]="tab() === 'collect'" (click)="setTab('collect')">Collect</button>
          <button class="tab" [class.active]="tab() === 'outstanding'" (click)="setTab('outstanding')">Outstanding</button>
          <button class="tab" [class.active]="tab() === 'payments'" (click)="setTab('payments')">Recent payments</button>
          @if (canSetup()) { <a class="tab" routerLink="/fees/setup">⚙ Fee setup (heads, plans, assign)</a> }
        }
      </div>

      @if (tab() === 'my-fees') {
        <section class="split">
          <article class="card">
            <div class="card-header"><h3>Fee ledger</h3></div>
            <div class="card-body">
              @if (!children().length) {
                <div class="empty">No children are linked to your account.</div>
              } @else {
                <div class="row" style="gap:8px;margin-bottom:14px">
                  @for (child of children(); track child.studentId) {
                    <button
                      class="btn btn-sm"
                      [class.btn-primary]="selectedChildId() === child.studentId"
                      [class.btn-ghost]="selectedChildId() !== child.studentId"
                      type="button"
                      (click)="selectChild(child.studentId)">
                      {{ child.fullName }}
                    </button>
                  }
                </div>

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
                    @if (l.platformFee > 0) {
                      <div><div class="small muted">Platform fee (included)</div><strong>{{ l.platformFee | currency:'INR':'symbol':'1.0-0' }}</strong></div>
                    }
                    @if (l.penaltyTotal > 0) {
                      <div>
                        <div class="small muted">Late fee applied</div>
                        <strong style="color:var(--amber-600)">{{ l.penaltyTotal | currency:'INR':'symbol':'1.0-0' }}</strong>
                      </div>
                    }
                  </div>
                  <div class="divider"></div>
                  <div class="list">
                    @for (installment of l.installments; track installment.id) {
                      <div class="list-item">
                        <div [class]="'dot ' + (installment.isOverdue ? 'red' : installment.balance <= 0 ? 'green' : '')"></div>
                        <div style="flex:1">
                          <div style="font-weight:600">{{ installment.name }}</div>
                          <div class="small muted">Due {{ installment.dueDate | date:'dd MMM yyyy' }} · {{ installment.status }}</div>
                          @if (installment.penaltyAmount > 0) {
                            <div class="small" style="color:var(--amber-600)">
                              Late fee {{ installment.penaltyAmount | currency:'INR':'symbol':'1.0-0' }} added
                            </div>
                          }
                        </div>
                        <div style="text-align:right">
                          <div>{{ installment.balance | currency:'INR':'symbol':'1.0-0' }}</div>
                          @if (installment.balance > 0) {
                            @if (onlineEnabled()) { <button class="btn btn-gold btn-sm" type="button" (click)="startOnlinePayment(installment.id, installment.balance)">
                              Pay online
                            </button> } @else { <div class="small muted">Pay at the school office</div> }
                          }
                        </div>
                      </div>
                    }
                  </div>
                  <div class="divider"></div>
                  <button class="btn btn-ghost btn-sm" type="button" (click)="downloadStatement(l.studentId)">
                    ⬇ Download fee statement / bill
                  </button>
                } @else {
                  <div class="loading">Loading ledger…</div>
                }
              }
            </div>
          </article>

          <article class="card">
            <div class="card-header"><h3>Receipts</h3></div>
            <div class="card-body">
              @if (ledger(); as l) {
                @if (!l.payments.length) {
                  <div class="empty">No payments recorded yet.</div>
                } @else {
                  <div class="list">
                    @for (payment of l.payments; track payment.id) {
                      <div class="list-item">
                        <div class="dot green"></div>
                        <div style="flex:1">
                          <div style="font-weight:600">{{ payment.receiptNo }}</div>
                          <div class="small muted">{{ payment.paidAtUtc | date:'dd MMM yyyy, HH:mm' }} · {{ payment.mode }}</div>
                        </div>
                        <div style="text-align:right">
                          <div>{{ payment.amount | currency:'INR':'symbol':'1.0-0' }}</div>
                          <button class="btn btn-ghost btn-sm" type="button" (click)="downloadReceipt(payment.id)">
                            ⬇ Download
                          </button>
                        </div>
                      </div>
                    }
                  </div>
                }
              } @else {
                <div class="empty">Select a child to see the receipts.</div>
              }
            </div>
          </article>
        </section>
      }

      @if (tab() === 'collect') {
        <section class="split">
          <article class="card">
            <div class="card-header">
              <h3>Record a payment</h3>
              @if (ledger(); as l) { <span class="badge">{{ l.studentName }}</span> }
            </div>
            <div class="card-body">
              <form [formGroup]="form" (ngSubmit)="pay()" class="form-grid">
                <label class="field full">
                  <span>Find student *</span>
                  <div class="row" style="flex-wrap:nowrap">
                    <input [value]="studentSearch()" (input)="studentSearch.set($any($event.target).value)"
                           (keyup.enter)="findStudent()" placeholder="Admission no. or name" />
                    <button class="btn btn-ghost btn-sm" type="button" (click)="findStudent()">Search</button>
                  </div>
                  @if (studentMatches().length > 1) {
                    <div class="row" style="flex-wrap:wrap;gap:6px;margin-top:6px">
                      @for (s of studentMatches(); track s.id) {
                        <button class="btn btn-ghost btn-sm" type="button" (click)="chooseStudent(s.id)">{{ s.fullName }} · {{ s.admissionNo }}</button>
                      }
                    </div>
                  }
                </label>
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
                  @if (l.platformFee > 0) {
                    <div><div class="small muted">Platform fee (included)</div><strong>{{ l.platformFee | currency:'INR':'symbol':'1.0-0' }}</strong></div>
                  }
                  @if (l.penaltyTotal > 0) {
                    <div>
                      <div class="small muted">Late fee applied</div>
                      <strong style="color:var(--amber-600)">{{ l.penaltyTotal | currency:'INR':'symbol':'1.0-0' }}</strong>
                    </div>
                  }
                </div>
                <div class="divider"></div>
                @if (canManage() && l.assignments.length) {
                  <div style="font-weight:650;margin-bottom:6px">Fee plans</div>
                  <div class="list">
                    @for (a of l.assignments; track a.id) {
                      <div class="list-item" style="flex-wrap:wrap">
                        <div [class]="'dot ' + (a.isActive ? 'green' : '')"></div>
                        <div style="flex:1">
                          <div style="font-weight:600">{{ a.planName }} @if (!a.isActive) { <span class="badge">Cancelled</span> }</div>
                          <div class="small muted">
                            Net {{ a.netAmount | currency:'INR':'symbol':'1.0-0' }} · Discount {{ a.discountAmount | currency:'INR':'symbol':'1.0-0' }}
                            @if (a.platformFeeAmount > 0) { · Platform fee {{ a.platformFeeAmount | currency:'INR':'symbol':'1.0-0' }} }
                            · Paid {{ a.paidAmount | currency:'INR':'symbol':'1.0-0' }}
                          </div>
                        </div>
                        @if (a.isActive) {
                          <div class="row" style="gap:6px">
                            <button class="btn btn-ghost btn-sm" type="button" (click)="openAction('discount', a.id)" [disabled]="acting()">Add discount</button>
                            <button class="btn btn-ghost btn-sm" type="button" (click)="openAction('cancel', a.id)" [disabled]="acting()">Cancel plan</button>
                          </div>
                        }
                        @if (action()?.id === a.id && (action()?.kind === 'discount' || action()?.kind === 'cancel')) {
                          <div class="row" style="gap:6px;flex-wrap:wrap;width:100%;margin-top:6px">
                            @if (action()?.kind === 'discount') {
                              <input type="number" min="0.01" step="0.01" [(ngModel)]="actAmount" aria-label="Discount amount" placeholder="Amount" style="max-width:120px" />
                            }
                            <input [(ngModel)]="actReason" aria-label="Reason" placeholder="Reason *" maxlength="500" style="flex:1;min-width:160px" />
                            <button class="btn btn-primary btn-sm" type="button" (click)="submitAction(l.studentId)" [disabled]="acting() || !actionValid()">
                              {{ acting() ? 'Saving…' : (action()?.kind === 'discount' ? 'Apply discount' : 'Cancel plan') }}
                            </button>
                            <button class="btn btn-ghost btn-sm" type="button" (click)="closeAction()" [disabled]="acting()">Close</button>
                          </div>
                        }
                      </div>
                    }
                  </div>
                  <div class="divider"></div>
                }
                <div class="list">
                  @for (installment of l.installments; track installment.id) {
                    <div class="list-item" style="flex-wrap:wrap">
                      <div [class]="'dot ' + (installment.isOverdue ? 'red' : installment.balance <= 0 ? 'green' : '')"></div>
                      <div style="flex:1">
                        <div style="font-weight:600">{{ installment.name }}</div>
                        <div class="small muted">Due {{ installment.dueDate | date:'dd MMM yyyy' }} · {{ installment.status }}</div>
                        @if (installment.penaltyAmount > 0) {
                          <div class="small" style="color:var(--amber-600)">
                            Late fee {{ installment.penaltyAmount | currency:'INR':'symbol':'1.0-0' }} added
                          </div>
                        }
                      </div>
                      <div style="text-align:right">
                        <div>{{ installment.balance | currency:'INR':'symbol':'1.0-0' }}</div>
                        @if (installment.balance > 0) {
                          <div class="row" style="gap:6px;justify-content:flex-end;margin-top:4px">
                            <button class="btn btn-ghost btn-sm" type="button" (click)="payInstallment(installment.id, installment.balance)">Pay</button>
                            @if (onlineEnabled()) { <button class="btn btn-gold btn-sm" type="button" (click)="startOnlinePayment(installment.id, installment.balance)">Pay online</button> }
                          </div>
                        }
                        @if (canManage() && isOpen(installment)) {
                          <div class="row" style="gap:6px;justify-content:flex-end;margin-top:4px">
                            <button class="btn btn-ghost btn-sm" type="button" (click)="openEdit(installment)" [disabled]="acting()">Edit</button>
                            <button class="btn btn-ghost btn-sm" type="button" (click)="openAction('waive', installment.id)" [disabled]="acting()">Waive</button>
                          </div>
                        }
                      </div>
                      @if (action()?.id === installment.id && (action()?.kind === 'edit' || action()?.kind === 'waive')) {
                        <div class="row" style="gap:6px;flex-wrap:wrap;width:100%;margin-top:6px">
                          @if (action()?.kind === 'edit') {
                            <input [(ngModel)]="actName" aria-label="Installment name" placeholder="Name *" maxlength="120" style="max-width:160px" />
                            <input type="date" [(ngModel)]="actDue" aria-label="Due date" />
                            <input type="number" min="0.01" step="0.01" [(ngModel)]="actAmount" aria-label="Amount" placeholder="Amount" style="max-width:120px" />
                            @if (installment.paidAmount > 0) {
                              <span class="small muted">Min {{ minAmount(installment) | currency:'INR':'symbol':'1.0-2' }} (already paid)</span>
                            }
                          } @else {
                            <input [(ngModel)]="actReason" aria-label="Reason" placeholder="Reason *" maxlength="500" style="flex:1;min-width:160px" />
                          }
                          <button class="btn btn-primary btn-sm" type="button" (click)="submitAction(l.studentId, installment)" [disabled]="acting() || !actionValid(installment)">
                            {{ acting() ? 'Saving…' : (action()?.kind === 'edit' ? 'Save' : 'Waive balance') }}
                          </button>
                          <button class="btn btn-ghost btn-sm" type="button" (click)="closeAction()" [disabled]="acting()">Close</button>
                        </div>
                      }
                    </div>
                  }
                </div>
                <div class="divider"></div>
                <div class="row">
                  <button class="btn btn-ghost btn-sm" type="button" (click)="downloadStatement(l.studentId)">
                    ⬇ Download fee statement
                  </button>
                  <span class="small muted">Printable bill with every installment and receipt.</span>
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
                <thead><tr><th>Receipt</th><th>Student</th><th>Mode</th><th>Paid</th><th>Amount</th><th>Status</th><th></th></tr></thead>
                <tbody>
                  @for (payment of payments(); track payment.id) {
                    <tr>
                      <td class="primary">{{ payment.receiptNo }}</td>
                      <td>{{ payment.studentName }}<div class="secondary">#{{ payment.studentId }}</div></td>
                      <td>{{ payment.mode }}</td>
                      <td class="small muted">{{ payment.paidAtUtc | date:'dd MMM, HH:mm' }}</td>
                      <td>{{ payment.amount | currency:'INR':'symbol':'1.0-0' }}</td>
                      <td><span class="badge green">{{ payment.status }}</span></td>
                      <td>
                        <button class="btn btn-ghost btn-sm" type="button" (click)="downloadReceipt(payment.id)">
                          ⬇ Receipt
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      }

      @if (checkout(); as order) {
        <div class="modal-backdrop" (click)="checkout.set(null)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-head">
              <h3>Secure checkout</h3>
              <span class="badge gold">{{ order.gateway }}</span>
            </div>
            <div class="modal-body">
              <div class="checkout-amount">{{ order.amount | currency:'INR':'symbol':'1.2-2' }}</div>
              <div class="small muted">{{ order.studentName }}</div>
              @if (order.installmentName) {
                <div class="small muted">Installment: {{ order.installmentName }}</div>
              }
              <div class="divider"></div>
              <label class="field">
                <span>Card / UPI reference</span>
                <input [(ngModel)]="cardReference" placeholder="Reference shown by your bank or UPI app" />
              </label>
              <p class="small muted">
                Paying through {{ order.gateway }}. The amount is verified against the order the server created.
              </p>
            </div>
            <div class="modal-foot">
              <button class="btn btn-ghost" type="button" (click)="checkout.set(null)" [disabled]="paying()">Cancel</button>
              <button class="btn btn-primary" type="button" (click)="completeOnlinePayment(order)" [disabled]="paying()">
                {{ paying() ? 'Processing…' : 'Pay ' + (order.amount | currency:'INR':'symbol':'1.0-0') }}
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .modal-backdrop { position: fixed; inset: 0; background: rgba(6, 20, 35, .55); display: grid; place-items: center; z-index: 70; padding: 20px; }
    .modal { width: min(420px, 100%); background: var(--surface); border-radius: var(--radius); box-shadow: var(--shadow); overflow: hidden; }
    .modal-head { display: flex; align-items: center; justify-content: space-between; padding: 15px 18px; border-bottom: 1px solid var(--line); }
    .modal-body { padding: 18px; display: grid; gap: 10px; }
    .modal-foot { display: flex; justify-content: flex-end; gap: 10px; padding: 13px 18px; border-top: 1px solid var(--line); background: #fbfdff; }
    .checkout-amount { font-size: 30px; font-weight: 700; letter-spacing: -.03em; }
  `]
})
export class FeesComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);

  readonly tab = signal<Tab>('collect');
  readonly ledger = signal<Ledger | null>(null);
  readonly outstanding = signal<Defaulter[]>([]);
  readonly payments = signal<Payment[]>([]);
  readonly classes = signal<ClassRoom[]>([]);
  readonly children = signal<Child[]>([]);
  readonly selectedChildId = signal<number | null>(null);
  readonly summary = signal<CollectionSummary | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly classId = signal('');
  readonly checkout = signal<PaymentOrder | null>(null);
  readonly paying = signal(false);
  cardReference = '';
  readonly onlineEnabled = signal(false);

  // Parents and students both get the family view: their own ledger, never school-wide figures.
  readonly isStudent = computed(() => this.auth.user()?.roleCode === 'Student');
  readonly isParent = computed(() => this.auth.user()?.roleCode === 'Parent' || this.isStudent());

  readonly modes: PaymentMode[] = ['Cash', 'Upi', 'Card', 'NetBanking', 'Cheque', 'BankTransfer', 'Online'];

  readonly form = this.fb.nonNullable.group({
    studentId: [0, [Validators.required, Validators.min(1)]],
    amount: [0, [Validators.required, Validators.min(1)]],
    mode: ['Cash' as PaymentMode, Validators.required],
    referenceNo: [''],
    remarks: ['']
  });

  private feeInstallmentId: number | null = null;

  readonly canSetup = computed(() => this.auth.has('fees.collect'));
  readonly canManage = computed(() => this.auth.has('fees.collect'));
  readonly action = signal<{ kind: LedgerAction; id: number } | null>(null);
  readonly acting = signal(false);
  actAmount: number | null = null;
  actReason = '';
  actName = '';
  actDue = '';

  isOpen(i: Installment): boolean {
    return i.status !== 'Paid' && i.status !== 'Waived';
  }

  minAmount(i: Installment): number {
    return Math.max(0, i.paidAmount - i.penaltyAmount);
  }

  openAction(kind: LedgerAction, id: number): void {
    this.action.set({ kind, id });
    this.actAmount = null;
    this.actReason = '';
  }

  openEdit(i: Installment): void {
    this.openAction('edit', i.id);
    this.actName = i.name;
    this.actDue = i.dueDate.substring(0, 10);
    this.actAmount = i.amount;
  }

  closeAction(): void {
    this.action.set(null);
  }

  actionValid(installment?: Installment): boolean {
    const a = this.action();
    if (!a) return false;
    const amount = Number(this.actAmount);
    switch (a.kind) {
      case 'cancel':
      case 'waive':
        return !!this.actReason.trim();
      case 'discount':
        return !!this.actReason.trim() && amount > 0;
      case 'edit':
        return !!this.actName.trim() && !!this.actDue && amount > 0 &&
          (!installment || amount >= this.minAmount(installment));
    }
  }

  submitAction(studentId: number, installment?: Installment): void {
    const a = this.action();
    if (!a || this.acting() || !this.actionValid(installment)) return;

    const reason = this.actReason.trim();
    const amount = Math.round(Number(this.actAmount) * 100) / 100;
    let request: Observable<Ledger>;
    let done: string;

    switch (a.kind) {
      case 'cancel':
        if (!confirm('Cancel this fee plan? All its installments will be removed from the ledger.')) return;
        request = this.api.post<Ledger>(`fees/assignments/${a.id}/cancel`, { reason });
        done = 'Fee plan cancelled.';
        break;
      case 'discount':
        request = this.api.post<Ledger>(`fees/assignments/${a.id}/discount`, { amount, reason });
        done = `Discount of ${amount.toLocaleString('en-IN')} applied.`;
        break;
      case 'waive':
        if (!confirm(`Waive the remaining balance of ${installment?.name ?? 'this installment'}?`)) return;
        request = this.api.post<Ledger>(`fees/installments/${a.id}/waive`, { reason });
        done = 'Installment balance waived.';
        break;
      case 'edit':
        request = this.api.put<Ledger>(`fees/installments/${a.id}`, { name: this.actName.trim(), dueDate: this.actDue, amount });
        done = 'Installment updated.';
        break;
    }

    this.acting.set(true);
    this.error.set(null);
    this.message.set(null);
    request.subscribe({
      next: () => {
        this.acting.set(false);
        this.action.set(null);
        this.message.set(done);
        this.form.patchValue({ studentId });
        this.loadLedger();
        this.loadOutstanding();
      },
      error: response => {
        this.acting.set(false);
        this.error.set(readError(response) ?? 'The change could not be saved.');
      }
    });
  }
  readonly studentSearch = signal('');
  readonly studentMatches = signal<{ id: number; fullName: string; admissionNo: string }[]>([]);

  findStudent(): void {
    const term = this.studentSearch().trim();
    if (!term) return;
    this.api.get<{ items: { id: number; fullName: string; admissionNo: string }[] }>('students', { search: term, pageSize: 10 })
      .subscribe({
        next: r => {
          this.studentMatches.set(r.items);
          if (r.items.length === 1) this.chooseStudent(r.items[0].id);
          else if (!r.items.length) this.error.set('No student found.');
        },
        error: () => this.error.set('Could not search students.')
      });
  }

  chooseStudent(id: number): void {
    this.form.controls.studentId.setValue(id);
    this.studentMatches.set([]);
    this.loadLedger();
  }

  ngOnInit(): void {
    const now = new Date();
    const from = new Date(now.getFullYear(), now.getMonth(), 1);

    this.api.get<{ onlineEnabled: boolean }>('fees/payment-config').subscribe({
      next: c => this.onlineEnabled.set(!!c.onlineEnabled),
      error: () => this.onlineEnabled.set(false)
    });

    if (this.isParent()) {
      this.tab.set('my-fees');
      this.loadChildren();
      this.loadPayments(from, now);
      return;
    }

    this.api.get<ClassRoom[]>('admin/classes').subscribe({ next: rows => this.classes.set(rows), error: () => this.error.set('Some information could not be loaded. Check your connection and refresh the page.') });
    this.api
      .get<CollectionSummary>('fees/summary', { from: iso(from), to: iso(now) })
      .subscribe({ next: value => this.summary.set(value), error: () => this.error.set('Some information could not be loaded. Check your connection and refresh the page.') });
    this.loadPayments(from, now);
    this.loadOutstanding();
  }

  private loadChildren(): void {
    const me = this.auth.user();
    if (this.isStudent() && me?.studentId) {
      this.children.set([{ studentId: me.studentId, fullName: me.fullName } as Child]);
      this.selectChild(me.studentId);
      return;
    }

    this.api.get<Child[]>('parent/children').subscribe({
      next: kids => {
        this.children.set(kids);
        if (kids.length) this.selectChild(kids[0].studentId);
      },
      error: () => this.error.set('Could not load your children.')
    });
  }

  selectChild(studentId: number): void {
    this.selectedChildId.set(studentId);
    this.form.patchValue({ studentId });
    this.loadLedger();
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
          if (!this.isParent()) this.loadOutstanding();
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

  /** Opens a payment session with the gateway and shows the checkout sheet. */
  startOnlinePayment(installmentId: number, amount: number): void {
    const studentId = this.form.controls.studentId.value;
    if (!studentId) {
      this.error.set('Load the student ledger first.');
      return;
    }

    this.error.set(null);
    this.api
      .post<PaymentOrder>('fees/payments/order', { studentId, amount, feeInstallmentId: installmentId })
      .subscribe({
        next: order => this.checkout.set(order),
        error: () => this.error.set('The payment session could not be opened.')
      });
  }

  completeOnlinePayment(order: PaymentOrder): void {
    if (this.paying()) return;

    this.paying.set(true);
    this.error.set(null);

    this.api
      .post<Payment>('fees/payments/online', {
        studentId: order.studentId,
        feeInstallmentId: null,
        amount: order.amount,
        gatewayOrderId: order.orderId,
        gatewayPaymentId: order.paymentId,
        idempotencyKey: crypto.randomUUID()
      })
      .subscribe({
        next: payment => {
          this.paying.set(false);
          this.checkout.set(null);
          this.message.set(`Payment of ${payment.amount.toLocaleString('en-IN')} received. Receipt ${payment.receiptNo}.`);
          this.loadLedger();
          // The outstanding list is school-wide and is not available to a parent.
          if (!this.isParent()) this.loadOutstanding();
          const now = new Date();
          this.loadPayments(new Date(now.getFullYear(), now.getMonth(), 1), now);
        },
        error: response => {
          this.paying.set(false);
          this.error.set(readError(response) ?? 'The payment could not be completed.');
        }
      });
  }

  downloadReceipt(paymentId: number): void {
    this.api.download(`fees/payments/${paymentId}/receipt`).subscribe({
      next: response => this.api.saveBlob(response, 'receipt.pdf'),
      error: () => this.error.set('The receipt could not be downloaded.')
    });
  }

  downloadStatement(studentId: number): void {
    this.api.download(`fees/students/${studentId}/statement`).subscribe({
      next: response => this.api.saveBlob(response, 'fee-statement.pdf'),
      error: () => this.error.set('The fee statement could not be downloaded.')
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
      .subscribe({ next: result => this.payments.set(result.items), error: () => this.error.set('Some information could not be loaded. Check your connection and refresh the page.') });
  }

  overdueTotal(): number {
    return this.outstanding().reduce((sum, row) => sum + row.outstanding, 0);
  }
}

function iso(date: Date): string {
  return localDate(date);
}

function readError(response: unknown): string | null {
  const body = (response as { error?: { detail?: string; title?: string; errors?: Record<string, string[]> } })?.error;
  if (!body) return null;
  if (body.errors) {
    const first = Object.values(body.errors)[0];
    if (first?.length) return first[0];
  }
  return body.detail ?? body.title ?? null;
}
