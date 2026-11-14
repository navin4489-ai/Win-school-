import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { localDate } from '../../core/dates';
import { readError } from '../platform/schools.component';

interface Vehicle {
  id: number; registrationNo: string; model?: string | null; capacity: number; driverName?: string | null;
  driverPhone?: string | null; insuranceExpiry?: string | null; fitnessExpiry?: string | null;
}
interface Route {
  id: number; name: string; code: string; vehicleId?: number | null; startPoint?: string | null;
  endPoint?: string | null; monthlyFee: number; stopCount: number;
}
interface Allocation {
  id: number; studentId: number; studentName: string; transportRouteId: number; routeName: string;
  startDate: string; isActive: boolean;
}
interface StudentRow { id: number; admissionNo: string; fullName: string; className: string; sectionName: string; }
type Tab = 'routes' | 'vehicles' | 'students';

/** Vehicles, routes and which students use which route. */
@Component({
  selector: 'app-transport',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, DatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Operations</div>
          <h1>Transport</h1>
          <p class="subtitle">Vehicles, routes and student transport allocation.</p>
        </div>
      </header>

      @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
      @if (message()) { <div class="success" role="status">{{ message() }}</div> }

      <div class="tabs" role="tablist">
        <button class="tab" role="tab" [class.active]="tab() === 'routes'" (click)="tab.set('routes')">Routes</button>
        <button class="tab" role="tab" [class.active]="tab() === 'vehicles'" (click)="tab.set('vehicles')">Vehicles</button>
        <button class="tab" role="tab" [class.active]="tab() === 'students'" (click)="setStudentsTab()">Students</button>
      </div>

      @if (tab() === 'vehicles') {
        <section class="card">
          <div class="card-header"><h3>Add vehicle</h3></div>
          <div class="card-body"><div class="form-grid">
            <label class="field"><span>Registration no. *</span><input [(ngModel)]="veh.registrationNo" placeholder="DL 01 AB 1234" /></label>
            <label class="field"><span>Model</span><input [(ngModel)]="veh.model" /></label>
            <label class="field"><span>Seats *</span><input type="number" min="1" max="200" [(ngModel)]="veh.capacity" /></label>
            <label class="field"><span>Driver name</span><input [(ngModel)]="veh.driverName" /></label>
            <label class="field"><span>Driver phone</span><input [(ngModel)]="veh.driverPhone" /></label>
            <label class="field"><span>Driving licence no.</span><input [(ngModel)]="veh.driverLicenceNo" /></label>
            <label class="field"><span>Insurance expiry</span><input type="date" [(ngModel)]="veh.insuranceExpiry" /></label>
            <label class="field"><span>Fitness expiry</span><input type="date" [(ngModel)]="veh.fitnessExpiry" /></label>
            <div class="full"><button class="btn btn-primary" (click)="addVehicle()"
              [disabled]="saving() || !veh.registrationNo.trim() || !(veh.capacity >= 1 && veh.capacity <= 200)">Add vehicle</button></div>
          </div></div>
        </section>
        <section class="card">
          <div class="card-header"><h3>Vehicles ({{ vehicles().length }})</h3></div>
          @if (!vehicles().length) { <div class="empty">No vehicles yet.</div> }
          @else {
            <div class="table-wrap"><table>
              <thead><tr><th>Registration</th><th>Model</th><th>Seats</th><th>Driver</th><th>Insurance</th><th>Fitness</th></tr></thead>
              <tbody>
                @for (v of vehicles(); track v.id) {
                  <tr>
                    <td class="primary">{{ v.registrationNo }}</td><td>{{ v.model || '—' }}</td><td>{{ v.capacity }}</td>
                    <td>{{ v.driverName || '—' }}{{ v.driverPhone ? ' · ' + v.driverPhone : '' }}</td>
                    <td [style.color]="expired(v.insuranceExpiry) ? 'var(--red-500)' : ''">{{ v.insuranceExpiry ? (v.insuranceExpiry | date:'dd MMM yyyy') : '—' }}{{ expired(v.insuranceExpiry) ? ' (expired)' : '' }}</td>
                    <td [style.color]="expired(v.fitnessExpiry) ? 'var(--red-500)' : ''">{{ v.fitnessExpiry ? (v.fitnessExpiry | date:'dd MMM yyyy') : '—' }}{{ expired(v.fitnessExpiry) ? ' (expired)' : '' }}</td>
                  </tr>
                }
              </tbody>
            </table></div>
          }
        </section>
      }

      @if (tab() === 'routes') {
        <section class="card">
          <div class="card-header"><h3>Add route</h3></div>
          <div class="card-body"><div class="form-grid">
            <label class="field"><span>Route name *</span><input [(ngModel)]="rt.name" placeholder="e.g. Sector 14 – School" /></label>
            <label class="field"><span>Code *</span><input [(ngModel)]="rt.code" placeholder="R1" /></label>
            <label class="field"><span>Vehicle</span>
              <select [(ngModel)]="rt.vehicleId">
                <option [ngValue]="null">Not assigned</option>
                @for (v of vehicles(); track v.id) { <option [ngValue]="v.id">{{ v.registrationNo }} ({{ v.capacity }} seats)</option> }
              </select>
            </label>
            <label class="field"><span>Start point</span><input [(ngModel)]="rt.startPoint" /></label>
            <label class="field"><span>End point</span><input [(ngModel)]="rt.endPoint" /></label>
            <label class="field"><span>Monthly fee (₹)</span><input type="number" min="0" [(ngModel)]="rt.monthlyFee" /></label>
            <div class="full"><button class="btn btn-primary" (click)="addRoute()"
              [disabled]="saving() || !rt.name.trim() || !rt.code.trim() || rt.monthlyFee < 0">Add route</button></div>
          </div></div>
        </section>
        <section class="card">
          <div class="card-header"><h3>Routes ({{ routes().length }})</h3></div>
          @if (!routes().length) { <div class="empty">No routes yet.</div> }
          @else {
            <div class="table-wrap"><table>
              <thead><tr><th>Route</th><th>Code</th><th>Vehicle</th><th>From → To</th><th>Monthly fee</th><th>Students</th></tr></thead>
              <tbody>
                @for (r of routes(); track r.id) {
                  <tr>
                    <td class="primary">{{ r.name }}</td><td>{{ r.code }}</td><td>{{ vehicleName(r.vehicleId) }}</td>
                    <td>{{ r.startPoint || '—' }} → {{ r.endPoint || '—' }}</td>
                    <td>{{ r.monthlyFee | currency:'INR':'symbol':'1.0-0' }}</td>
                    <td>{{ ridersOn(r.id) }}{{ capacityOf(r.vehicleId) ? ' / ' + capacityOf(r.vehicleId) : '' }}</td>
                  </tr>
                }
              </tbody>
            </table></div>
          }
        </section>
      }

      @if (tab() === 'students') {
        <section class="card">
          <div class="card-header"><h3>Assign a student to a route</h3></div>
          <div class="card-body">
            <div class="row" style="gap:8px;flex-wrap:wrap">
              <input [(ngModel)]="search" (keyup.enter)="findStudents()" aria-label="Search student"
                     placeholder="Admission no. or name" style="width:100%;max-width:240px" />
              <button class="btn btn-ghost btn-sm" (click)="findStudents()">Search</button>
              <select [(ngModel)]="allocRouteId" aria-label="Route">
                <option [ngValue]="0">Select route</option>
                @for (r of routes(); track r.id) { <option [ngValue]="r.id">{{ r.name }}</option> }
              </select>
              <input type="date" [(ngModel)]="allocStart" aria-label="Start date" />
            </div>
            @if (found().length) {
              <div class="list" style="margin-top:8px">
                @for (s of found(); track s.id) {
                  <div class="list-item">
                    <div style="flex:1"><strong>{{ s.fullName }}</strong> <span class="small muted">{{ s.admissionNo }} · {{ s.className }} {{ s.sectionName }}</span></div>
                    <button class="btn btn-primary btn-sm" [disabled]="saving() || !allocRouteId" (click)="allocate(s)">Assign</button>
                  </div>
                }
              </div>
            }
          </div>
        </section>
        <section class="card">
          <div class="card-header"><h3>Students using transport ({{ allocations().length }})</h3></div>
          @if (!allocations().length) { <div class="empty">No students assigned yet.</div> }
          @else {
            <div class="table-wrap"><table>
              <thead><tr><th>Student</th><th>Route</th><th>From</th><th>Status</th></tr></thead>
              <tbody>
                @for (a of allocations(); track a.id) {
                  <tr><td class="primary">{{ a.studentName }}</td><td>{{ a.routeName }}</td>
                    <td>{{ a.startDate | date:'dd MMM yyyy' }}</td>
                    <td><span class="badge" [class.teal]="a.isActive">{{ a.isActive ? 'Active' : 'Ended' }}</span></td></tr>
                }
              </tbody>
            </table></div>
          }
        </section>
      }
    </div>
  `
})
export class TransportComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly tab = signal<Tab>('routes');
  readonly vehicles = signal<Vehicle[]>([]);
  readonly routes = signal<Route[]>([]);
  readonly allocations = signal<Allocation[]>([]);
  readonly found = signal<StudentRow[]>([]);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  veh = this.blankVehicle();
  rt = this.blankRoute();
  search = '';
  allocRouteId = 0;
  allocStart = localDate();

  ngOnInit(): void {
    this.loadVehicles();
    this.loadRoutes();
    this.loadAllocations();
  }

  setStudentsTab(): void { this.tab.set('students'); this.loadAllocations(); }

  loadVehicles(): void { this.api.get<Vehicle[]>('transport/vehicles').subscribe({ next: v => this.vehicles.set(v), error: r => this.fail(r) }); }
  loadRoutes(): void { this.api.get<Route[]>('transport/routes').subscribe({ next: r => this.routes.set(r), error: e => this.fail(e) }); }
  loadAllocations(): void { this.api.get<Allocation[]>('transport/allocations').subscribe({ next: a => this.allocations.set(a), error: r => this.fail(r) }); }

  vehicleName(id?: number | null): string { return this.vehicles().find(v => v.id === id)?.registrationNo ?? '—'; }
  capacityOf(id?: number | null): number { return this.vehicles().find(v => v.id === id)?.capacity ?? 0; }
  ridersOn(routeId: number): number { return this.allocations().filter(a => a.transportRouteId === routeId && a.isActive).length; }
  expired(date?: string | null): boolean { return !!date && date < localDate(); }

  addVehicle(): void {
    if (this.saving()) return;
    this.saving.set(true);
    const v = this.veh;
    this.api.post('transport/vehicles', {
      ...v, registrationNo: v.registrationNo.trim().toUpperCase(), capacity: Number(v.capacity),
      model: v.model.trim() || null, driverName: v.driverName.trim() || null, driverPhone: v.driverPhone.trim() || null,
      driverLicenceNo: v.driverLicenceNo.trim() || null, insuranceExpiry: v.insuranceExpiry || null, fitnessExpiry: v.fitnessExpiry || null
    }).subscribe({
      next: () => { this.saving.set(false); this.ok(`Vehicle ${v.registrationNo.toUpperCase()} added.`); this.veh = this.blankVehicle(); this.loadVehicles(); },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  addRoute(): void {
    if (this.saving()) return;
    this.saving.set(true);
    const r = this.rt;
    this.api.post('transport/routes', {
      name: r.name.trim(), code: r.code.trim().toUpperCase(), vehicleId: r.vehicleId,
      startPoint: r.startPoint.trim() || null, endPoint: r.endPoint.trim() || null, monthlyFee: Number(r.monthlyFee) || 0
    }).subscribe({
      next: () => { this.saving.set(false); this.ok(`Route ${r.name} added.`); this.rt = this.blankRoute(); this.loadRoutes(); },
      error: e => { this.saving.set(false); this.fail(e); }
    });
  }

  findStudents(): void {
    if (!this.search.trim()) return;
    this.api.get<{ items: StudentRow[] }>('students', { search: this.search.trim(), pageSize: 10 }).subscribe({
      next: r => { this.found.set(r.items); if (!r.items.length) this.fail(null, 'No student found.'); },
      error: r => this.fail(r)
    });
  }

  allocate(s: StudentRow): void {
    if (this.saving() || !this.allocRouteId) return;
    const route = this.routes().find(r => r.id === this.allocRouteId);
    const cap = this.capacityOf(route?.vehicleId);
    if (cap && this.ridersOn(this.allocRouteId) >= cap && !confirm(`${route?.name} is already full (${cap} seats). Assign anyway?`)) return;
    this.saving.set(true);
    this.api.post('transport/allocations', { studentId: s.id, transportRouteId: this.allocRouteId, startDate: this.allocStart || null }).subscribe({
      next: () => { this.saving.set(false); this.ok(`${s.fullName} assigned to ${route?.name}.`); this.found.set([]); this.loadAllocations(); },
      error: r => { this.saving.set(false); this.fail(r); }
    });
  }

  private blankVehicle() {
    return { registrationNo: '', model: '', capacity: 40, driverName: '', driverPhone: '', driverLicenceNo: '', insuranceExpiry: '', fitnessExpiry: '' };
  }
  private blankRoute() { return { name: '', code: '', vehicleId: null as number | null, startPoint: '', endPoint: '', monthlyFee: 0 }; }
  private ok(text: string): void { this.error.set(null); this.message.set(text); }
  private fail(r: unknown, text?: string): void { this.message.set(null); this.error.set(text ?? readError(r)); }
}
