import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { readError } from '../platform/schools.component';

type Frequency = 'Monthly' | 'Quarterly' | 'HalfYearly' | 'Annual' | 'OneTime';
interface Category { id: number; name: string; code: string; isRecurring: boolean; }
interface Session { id: number; name: string; startDate: string; endDate: string; isCurrent: boolean; }
interface ClassRoom { id: number; name: string; }
interface PlanItem { feeCategoryId: number; categoryName: string; amount: number; frequency: Frequency; }
interface Plan {
  id: number; academicSessionId: number; sessionName: string; classId: number; className: string;
  name: string; totalAmount: number; isActive: boolean; items: PlanItem[];
}
interface StudentRow { id: number; admissionNo: string; fullName: string; className: string; sectionName: string; }
interface Installment { name: string; dueDate: string; amount: number; }

/** Fee heads, class fee plans and assigning a plan to a student. */
@Component({
  selector: 'app-fee-setup',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, RouterLink],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Finance</div>
          <h1>Fee setup</h1>
          <p class="subtitle">1. Create fee heads → 2. Build a fee plan for each class → 3. Assign the plan to students.</p>
        </div>
        <div class="toolbar"><a class="btn btn-ghost btn-sm" routerLink="/fees">← Back to fees</a></div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      <!-- 1. Fee heads -->
      <section class="card">
        <div class="card-header"><h3>1. Fee heads</h3></div>
        <div class="card-body">
          <div class="row" style="flex-wrap:wrap;gap:6px;margin-bottom:12px">
            @for (c of categories(); track c.id) {
              <span class="badge" [class.teal]="c.isRecurring">{{ c.name }} ({{ c.code }}){{ c.isRecurring ? ' · recurring' : '' }}</span>
            } @empty { <span class="small muted">No fee heads yet. Add e.g. Tuition fee, Annual fee, Admission fee.</span> }
          </div>
          <div class="row" style="gap:8px;flex-wrap:wrap">
            <input [(ngModel)]="catName" placeholder="Name, e.g. Annual fee" style="width:200px" />
            <input [(ngModel)]="catCode" placeholder="Code, e.g. ANNUAL" style="width:140px" />
            <label class="row" style="gap:6px"><input type="checkbox" [(ngModel)]="catRecurring" /> Recurring (every term/month)</label>
            <button class="btn btn-primary btn-sm" (click)="addCategory()" [disabled]="!catName.trim() || !catCode.trim()">Add fee head</button>
          </div>
        </div>
      </section>

      <!-- 2. Fee plans -->
      <section class="card">
        <div class="card-header"><h3>2. Fee plans</h3></div>
        <div class="card-body">
          <div class="form-grid">
            <label class="field"><span>Plan name *</span><input [(ngModel)]="planName" placeholder="e.g. Class 1 – 2026-27 annual fees" /></label>
            <label class="field"><span>Class *</span>
              <select [(ngModel)]="planClassId">
                <option [ngValue]="0">Select class</option>
                @for (c of classes(); track c.id) { <option [ngValue]="c.id">{{ c.name }}</option> }
              </select>
            </label>
            <label class="field"><span>Academic session *</span>
              <select [(ngModel)]="planSessionId">
                @for (s of sessions(); track s.id) { <option [ngValue]="s.id">{{ s.name }}{{ s.isCurrent ? ' (current)' : '' }}</option> }
              </select>
            </label>
          </div>
          <table style="margin-top:10px">
            <thead><tr><th>Fee head</th><th>Amount for the year (₹)</th><th>Type</th><th></th></tr></thead>
            <tbody>
              @for (line of planLines; track $index) {
                <tr>
                  <td>
                    <select [(ngModel)]="line.feeCategoryId">
                      <option [ngValue]="0">Select fee head</option>
                      @for (c of categories(); track c.id) { <option [ngValue]="c.id">{{ c.name }}</option> }
                    </select>
                  </td>
                  <td><input type="number" min="0" [(ngModel)]="line.amount" style="width:140px" /></td>
                  <td>
                    <select [(ngModel)]="line.frequency">
                      <option value="Annual">Annual</option>
                      <option value="OneTime">One time (e.g. admission)</option>
                      <option value="HalfYearly">Half-yearly</option>
                      <option value="Quarterly">Quarterly</option>
                      <option value="Monthly">Monthly</option>
                    </select>
                  </td>
                  <td><button class="btn btn-ghost btn-sm" (click)="planLines.splice($index, 1)" [disabled]="planLines.length === 1">Remove</button></td>
                </tr>
              }
            </tbody>
          </table>
          <div class="row" style="gap:10px;margin-top:10px">
            <button class="btn btn-ghost btn-sm" (click)="planLines.push(newLine())">+ Add fee head line</button>
            <strong>Total: {{ planTotal() | currency:'INR':'symbol':'1.0-0' }}</strong>
            <button class="btn btn-primary btn-sm" (click)="savePlan()" [disabled]="!canSavePlan()">Save fee plan</button>
          </div>

          <div class="table-wrap" style="margin-top:16px">
            <table>
              <thead><tr><th>Plan</th><th>Class</th><th>Session</th><th>Fee heads</th><th>Total</th></tr></thead>
              <tbody>
                @for (p of plans(); track p.id) {
                  <tr>
                    <td class="primary">{{ p.name }}</td><td>{{ p.className }}</td><td>{{ p.sessionName }}</td>
                    <td class="small">@for (i of p.items; track i.feeCategoryId) { {{ i.categoryName }}: {{ i.amount | currency:'INR':'symbol':'1.0-0' }}<br /> }</td>
                    <td><strong>{{ p.totalAmount | currency:'INR':'symbol':'1.0-0' }}</strong></td>
                  </tr>
                } @empty { <tr><td colspan="5" class="muted">No fee plans yet.</td></tr> }
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <!-- 3. Assign -->
      <section class="card">
        <div class="card-header"><h3>3. Assign fees to a student</h3></div>
        <div class="card-body">
          <div class="row" style="gap:8px;flex-wrap:wrap">
            <input [(ngModel)]="search" placeholder="Admission no. or student name" style="width:260px" (keyup.enter)="findStudents()" />
            <button class="btn btn-ghost btn-sm" (click)="findStudents()">Search</button>
          </div>
          @if (students().length) {
            <div class="list" style="margin-top:8px">
              @for (s of students(); track s.id) {
                <button class="list-item" style="width:100%;text-align:left;border:0;background:none;cursor:pointer"
                        [style.background]="student()?.id === s.id ? 'var(--teal-100)' : ''" (click)="pickStudent(s)">
                  <strong>{{ s.fullName }}</strong>&nbsp;<span class="small muted">{{ s.admissionNo }} · {{ s.className }} {{ s.sectionName }}</span>
                </button>
              }
            </div>
          }

          @if (student(); as st) {
            <div class="form-grid" style="margin-top:12px">
              <label class="field"><span>Fee plan for {{ st.className }} *</span>
                <select [(ngModel)]="assignPlanId" (ngModelChange)="buildSchedule()">
                  <option [ngValue]="0">Select plan</option>
                  @for (p of plansForStudent(); track p.id) {
                    <option [ngValue]="p.id">{{ p.name }} – {{ p.totalAmount | currency:'INR':'symbol':'1.0-0' }}</option>
                  }
                </select>
                @if (!plansForStudent().length) { <span class="hint">No plan for {{ st.className }} yet. Create one in step 2.</span> }
              </label>
              <label class="field"><span>Discount (₹)</span>
                <input type="number" min="0" [(ngModel)]="discount" (ngModelChange)="buildSchedule()" />
              </label>
              <label class="field"><span>Pay in</span>
                <select [(ngModel)]="schedule" (ngModelChange)="buildSchedule()">
                  <option value="1">1 payment (full year)</option>
                  <option value="2">2 half-yearly payments</option>
                  <option value="4">4 quarterly payments</option>
                  <option value="12">12 monthly payments</option>
                </select>
              </label>
              <label class="field"><span>First due date</span>
                <input type="date" [(ngModel)]="firstDue" (ngModelChange)="buildSchedule()" />
              </label>
            </div>

            @if (installments.length) {
              <table style="margin-top:10px">
                <thead><tr><th>Installment</th><th>Due date</th><th>Amount (₹)</th></tr></thead>
                <tbody>
                  @for (i of installments; track $index) {
                    <tr>
                      <td><input [(ngModel)]="i.name" style="width:160px" /></td>
                      <td><input type="date" [(ngModel)]="i.dueDate" /></td>
                      <td><input type="number" [(ngModel)]="i.amount" style="width:120px" /></td>
                    </tr>
                  }
                </tbody>
              </table>
              <div class="row" style="gap:10px;margin-top:10px">
                <span class="small" [style.color]="scheduleMismatch() ? 'var(--red-500)' : ''">
                  Installments total {{ scheduleTotal() | currency:'INR':'symbol':'1.0-0' }} / payable {{ payable() | currency:'INR':'symbol':'1.0-0' }}
                </span>
                <button class="btn btn-primary btn-sm" (click)="assign()" [disabled]="scheduleMismatch() || saving()">Assign fees</button>
              </div>
              <p class="small muted">A platform fee, if the school has one, is added on top automatically.</p>
            }
          }
        </div>
      </section>
    </div>
  `
})
export class FeeSetupComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly categories = signal<Category[]>([]);
  readonly sessions = signal<Session[]>([]);
  readonly classes = signal<ClassRoom[]>([]);
  readonly plans = signal<Plan[]>([]);
  readonly students = signal<StudentRow[]>([]);
  readonly student = signal<StudentRow | null>(null);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly saving = signal(false);

  catName = ''; catCode = ''; catRecurring = false;
  planName = ''; planClassId = 0; planSessionId = 0;
  planLines: { feeCategoryId: number; amount: number; frequency: Frequency }[] = [this.newLine()];
  search = '';
  assignPlanId = 0; discount = 0; schedule = '4'; firstDue = today();
  installments: Installment[] = [];

  readonly plansForStudent = computed(() =>
    this.plans().filter(p => p.isActive && p.className === this.student()?.className));

  ngOnInit(): void {
    this.loadCategories();
    this.loadPlans();
    this.api.get<ClassRoom[]>('admin/classes').subscribe({ next: c => this.classes.set(c) });
    this.api.get<Session[]>('admin/sessions').subscribe({
      next: s => {
        this.sessions.set(s);
        this.planSessionId = (s.find(x => x.isCurrent) ?? s[0])?.id ?? 0;
      }
    });
  }

  newLine() { return { feeCategoryId: 0, amount: 0, frequency: 'Annual' as Frequency }; }
  planTotal(): number { return this.planLines.reduce((t, l) => t + (Number(l.amount) || 0), 0); }
  canSavePlan(): boolean {
    return !!this.planName.trim() && this.planClassId > 0 && this.planSessionId > 0 &&
      this.planLines.every(l => l.feeCategoryId > 0 && Number(l.amount) > 0);
  }

  loadCategories(): void { this.api.get<Category[]>('fees/categories').subscribe({ next: c => this.categories.set(c) }); }
  loadPlans(): void { this.api.get<Plan[]>('fees/structures').subscribe({ next: p => this.plans.set(p), error: r => this.fail(r) }); }

  addCategory(): void {
    this.api.post('fees/categories', { name: this.catName.trim(), code: this.catCode.trim().toUpperCase(), isRecurring: this.catRecurring })
      .subscribe({
        next: () => { this.ok(`Fee head "${this.catName.trim()}" added.`); this.catName = ''; this.catCode = ''; this.catRecurring = false; this.loadCategories(); },
        error: r => this.fail(r)
      });
  }

  savePlan(): void {
    const body = {
      name: this.planName.trim(), classId: this.planClassId, academicSessionId: this.planSessionId,
      items: this.planLines.map(l => ({ feeCategoryId: l.feeCategoryId, amount: Number(l.amount), frequency: l.frequency, dueDay: 10 }))
    };
    this.api.post('fees/structures', body).subscribe({
      next: () => { this.ok(`Fee plan "${body.name}" saved.`); this.planName = ''; this.planLines = [this.newLine()]; this.loadPlans(); },
      error: r => this.fail(r)
    });
  }

  findStudents(): void {
    if (!this.search.trim()) return;
    this.api.get<{ items: StudentRow[] }>('students', { search: this.search.trim(), pageSize: 10 }).subscribe({
      next: r => { this.students.set(r.items); if (!r.items.length) this.fail(null, 'No student found.'); },
      error: r => this.fail(r)
    });
  }

  pickStudent(s: StudentRow): void {
    this.student.set(s);
    this.assignPlanId = this.plansForStudent()[0]?.id ?? 0;
    this.buildSchedule();
  }

  payable(): number {
    const plan = this.plans().find(p => p.id === this.assignPlanId);
    return plan ? Math.max(0, plan.totalAmount - (Number(this.discount) || 0)) : 0;
  }
  scheduleTotal(): number { return round(this.installments.reduce((t, i) => t + (Number(i.amount) || 0), 0)); }
  scheduleMismatch(): boolean { return !this.assignPlanId || Math.abs(this.scheduleTotal() - this.payable()) > 0.01; }

  buildSchedule(): void {
    const total = this.payable();
    const count = Number(this.schedule) || 1;
    if (!total) { this.installments = []; return; }
    const each = Math.floor((total / count) * 100) / 100;
    const step = 12 / count;
    const labels: Record<number, string> = { 1: 'Annual fee', 2: 'Half-year', 4: 'Quarter', 12: 'Month' };
    this.installments = Array.from({ length: count }, (_, i) => ({
      name: count === 1 ? labels[1] : `${labels[count]} ${i + 1}`,
      dueDate: addMonths(this.firstDue, i * step),
      amount: i === count - 1 ? round(total - each * (count - 1)) : each
    }));
  }

  assign(): void {
    const st = this.student();
    if (!st) return;
    this.saving.set(true);
    this.api.post('fees/assignments', {
      studentId: st.id, feeStructureId: this.assignPlanId, discountAmount: Number(this.discount) || 0,
      installments: this.installments.map(i => ({ name: i.name, dueDate: i.dueDate, amount: Number(i.amount) }))
    }).subscribe({
      next: () => { this.saving.set(false); this.ok(`Fees assigned to ${st.fullName}. Collect payments from the Fees page.`); this.installments = []; this.assignPlanId = 0; },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  private ok(text: string): void { this.error.set(null); this.message.set(text); }
  private fail(r: unknown, text?: string): void { this.message.set(null); this.error.set(text ?? readError(r)); }
}

function today(): string { return new Date().toISOString().slice(0, 10); }
function round(n: number): number { return Math.round(n * 100) / 100; }
function addMonths(iso: string, months: number): string {
  const d = new Date(iso + 'T00:00:00');
  d.setMonth(d.getMonth() + months);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
