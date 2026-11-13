import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';

interface Module { key: string; label: string; }
interface School {
  id: number; code: string; name: string; legalName?: string | null; logoUrl?: string | null;
  email?: string | null; phone?: string | null; addressLine1?: string | null; city?: string | null;
  state?: string | null; postalCode?: string | null; isActive: boolean; enabledModules: string[];
  userCount: number; studentCount: number;
}

/** Super admin: onboard schools, brand them and switch their modules on or off. */
@Component({
  selector: 'app-schools',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Platform</div>
          <h1>Schools</h1>
          <p class="subtitle">Each school has its own data, users and modules. Schools never see each other's records.</p>
        </div>
        <div class="toolbar">
          <button class="btn btn-primary btn-sm" (click)="formOpen() ? close() : openNew()">
            {{ formOpen() ? 'Close' : 'New school' }}
          </button>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      @if (formOpen()) {
        <section class="card">
          <div class="card-header"><h3>{{ editingId() ? 'Edit school' : 'New school' }}</h3></div>
          <div class="card-body">
            <form class="form-grid" [formGroup]="form" (ngSubmit)="save()">
              <label class="field">
                <span>School code *</span>
                <input formControlName="code" placeholder="e.g. GREENFIELD" [readonly]="!!editingId()" />
                <span class="hint">Used at sign-in. Cannot be changed later.</span>
              </label>
              <label class="field"><span>Name *</span><input formControlName="name" /></label>
              <label class="field"><span>Legal name</span><input formControlName="legalName" /></label>
              <label class="field"><span>Email</span><input formControlName="email" type="email" /></label>
              <label class="field"><span>Phone</span><input formControlName="phone" /></label>
              <label class="field"><span>Address</span><input formControlName="addressLine1" /></label>
              <label class="field"><span>City</span><input formControlName="city" /></label>
              <label class="field"><span>State</span><input formControlName="state" /></label>
              <label class="field"><span>PIN code</span><input formControlName="postalCode" /></label>

              <div class="field">
                <span>Logo</span>
                <div class="row" style="gap:10px">
                  @if (form.controls.logoUrl.value) {
                    <img [src]="form.controls.logoUrl.value" alt="logo" style="height:48px;width:48px;object-fit:contain;border-radius:8px;background:#fff" />
                  }
                  <input type="file" accept="image/*" (change)="uploadLogo($event)" />
                </div>
                @if (uploading()) { <span class="hint">Uploading…</span> }
              </div>

              <div class="field full">
                <span>Modules</span>
                <div class="row" style="flex-wrap:wrap;gap:14px">
                  @for (m of modules(); track m.key) {
                    <label class="row" style="gap:6px">
                      <input type="checkbox" [checked]="selected().has(m.key)" (change)="toggle(m.key)" />
                      <span>{{ m.label }}</span>
                    </label>
                  }
                </div>
                <span class="hint">Students, users and settings are always on. Changes apply at the users' next sign-in.</span>
              </div>

              @if (!editingId()) {
                <label class="field"><span>Admin full name *</span><input formControlName="adminFullName" /></label>
                <label class="field"><span>Admin email *</span><input formControlName="adminEmail" type="email" /></label>
                <label class="field">
                  <span>Admin password *</span><input formControlName="adminPassword" type="password" />
                  <span class="hint">At least 8 characters with letters and digits.</span>
                </label>
              }

              <div class="full row">
                <button class="btn btn-primary" type="submit" [disabled]="saving() || form.invalid">
                  {{ saving() ? 'Saving…' : (editingId() ? 'Update school' : 'Create school') }}
                </button>
                <button class="btn btn-ghost" type="button" (click)="close()">Cancel</button>
              </div>
            </form>
          </div>
        </section>
      }

      <section class="card">
        <div class="card-header">
          <h3>All schools</h3>
          <button class="btn btn-ghost btn-sm" (click)="load()">Refresh</button>
        </div>
        @if (loading()) {
          <div class="loading">Loading schools…</div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead><tr><th>School</th><th>Code</th><th>City</th><th>Users</th><th>Students</th><th>Modules</th><th>Status</th><th></th></tr></thead>
              <tbody>
                @for (s of schools(); track s.id) {
                  <tr>
                    <td>
                      <div class="row" style="gap:10px">
                        @if (s.logoUrl) { <img [src]="s.logoUrl" alt="" style="height:32px;width:32px;object-fit:contain" /> }
                        <div class="primary">{{ s.name }}</div>
                      </div>
                    </td>
                    <td>{{ s.code }}</td>
                    <td>{{ s.city || '—' }}</td>
                    <td>{{ s.userCount }}</td>
                    <td>{{ s.studentCount }}</td>
                    <td>{{ s.enabledModules.length }} / {{ modules().length }}</td>
                    <td>
                      <span class="badge" [class.teal]="s.isActive">{{ s.isActive ? 'Active' : 'Suspended' }}</span>
                    </td>
                    <td>
                      <div class="row" style="gap:6px;justify-content:flex-end">
                        <button class="btn btn-ghost btn-sm" (click)="edit(s)">Edit</button>
                        <a class="btn btn-ghost btn-sm" [routerLink]="['/users']" [queryParams]="{ schoolId: s.id }">Users</a>
                        <button class="btn btn-sm" [class.btn-danger]="s.isActive" [class.btn-ghost]="!s.isActive" (click)="setActive(s)">
                          {{ s.isActive ? 'Suspend' : 'Activate' }}
                        </button>
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
export class SchoolsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly schools = signal<School[]>([]);
  readonly modules = signal<Module[]>([]);
  readonly selected = signal<Set<string>>(new Set());
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly uploading = signal(false);
  readonly formOpen = signal(false);
  readonly editingId = signal<number | null>(null);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9-]{2,20}$/)]],
    name: ['', Validators.required],
    legalName: [''], email: [''], phone: [''], addressLine1: [''], city: [''], state: [''], postalCode: [''],
    logoUrl: [''],
    adminFullName: [''], adminEmail: [''], adminPassword: ['']
  });

  ngOnInit(): void {
    this.api.get<Module[]>('platform/modules').subscribe({ next: m => this.modules.set(m) });
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<School[]>('platform/schools').subscribe({
      next: items => { this.schools.set(items); this.loading.set(false); },
      error: r => { this.error.set(readError(r)); this.loading.set(false); }
    });
  }

  openNew(): void {
    this.editingId.set(null);
    this.form.reset();
    this.setAdminRequired(true);
    this.selected.set(new Set(this.modules().map(m => m.key)));
    this.formOpen.set(true);
  }

  edit(s: School): void {
    this.editingId.set(s.id);
    this.setAdminRequired(false);
    this.form.reset({
      code: s.code, name: s.name, legalName: s.legalName ?? '', email: s.email ?? '', phone: s.phone ?? '',
      addressLine1: s.addressLine1 ?? '', city: s.city ?? '', state: s.state ?? '', postalCode: s.postalCode ?? '',
      logoUrl: s.logoUrl ?? ''
    });
    this.selected.set(new Set(s.enabledModules));
    this.formOpen.set(true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  close(): void {
    this.formOpen.set(false);
    this.editingId.set(null);
  }

  toggle(key: string): void {
    const next = new Set(this.selected());
    next.has(key) ? next.delete(key) : next.add(key);
    this.selected.set(next);
  }

  uploadLogo(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const data = new FormData();
    data.append('file', file);
    this.uploading.set(true);
    this.api.upload<{ url: string }>('files/upload', data).subscribe({
      next: r => { this.form.controls.logoUrl.setValue(r.url); this.uploading.set(false); },
      error: r => { this.error.set(readError(r)); this.uploading.set(false); }
    });
  }

  save(): void {
    if (this.form.invalid || this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.message.set(null);

    const raw = this.form.getRawValue();
    const body = { ...raw, enabledModules: [...this.selected()] };
    const id = this.editingId();
    const request = id
      ? this.api.put<School>(`platform/schools/${id}`, body)
      : this.api.post<School>('platform/schools', body);

    request.subscribe({
      next: s => {
        this.saving.set(false);
        this.close();
        this.message.set(id ? 'School updated.' : `School ${s.code} created. The admin can sign in with school code ${s.code}.`);
        this.load();
      },
      error: r => { this.saving.set(false); this.error.set(readError(r)); }
    });
  }

  setActive(s: School): void {
    if (s.isActive && !confirm(`Suspend ${s.name}? All its users will be signed out.`)) return;
    this.api.post(`platform/schools/${s.id}/active`, { isActive: !s.isActive }).subscribe({
      next: () => { this.message.set(s.isActive ? 'School suspended.' : 'School activated.'); this.load(); },
      error: r => this.error.set(readError(r))
    });
  }

  private setAdminRequired(required: boolean): void {
    const c = this.form.controls;
    c.adminFullName.setValidators(required ? [Validators.required] : []);
    c.adminEmail.setValidators(required ? [Validators.required, Validators.email] : []);
    c.adminPassword.setValidators(required ? [Validators.required, Validators.minLength(8)] : []);
    [c.adminFullName, c.adminEmail, c.adminPassword].forEach(x => x.updateValueAndValidity());
  }
}

export function readError(response: unknown): string {
  const body = (response as { error?: { detail?: string; title?: string; errors?: Record<string, string[]> } })?.error;
  if (!body) return 'The request could not be completed.';
  const first = body.errors ? Object.values(body.errors)[0]?.[0] : undefined;
  return body.detail ?? first ?? body.title ?? 'The request could not be completed.';
}
