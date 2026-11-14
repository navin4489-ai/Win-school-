import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { readError } from './schools.component';

interface Role { id: number; code: string; name: string; permissionCount: number; }
interface User {
  id: number; schoolId: number; fullName: string; email: string; phone?: string | null;
  roleId: number; roleCode: string; roleName: string; isActive: boolean;
  lastLoginAtUtc?: string | null; isLockedOut: boolean;
}
interface SchoolOption { id: number; code: string; name: string; }
interface RoleWithPerms { id: number; name: string; code: string; permissions: string[]; }
interface Permission { id: number; code: string; module: string; action: string; description?: string | null; }

/** Users and roles of one school. The super admin can switch between schools. */
@Component({
  selector: 'app-users',
  standalone: true,
  imports: [ReactiveFormsModule, DatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Administration</div>
          <h1>Users &amp; roles</h1>
          <p class="subtitle">Create sign-ins and decide what each person can do.</p>
        </div>
        <div class="toolbar">
          @if (isPlatformAdmin) {
            <select class="btn btn-ghost btn-sm" [value]="schoolId() ?? ''" (change)="onSchool($event)">
              @for (s of schools(); track s.id) { <option [value]="s.id">{{ s.name }} ({{ s.code }})</option> }
            </select>
          }
          <button class="btn btn-primary btn-sm" (click)="formOpen() ? close() : openNew()">
            {{ formOpen() ? 'Close' : 'New user' }}
          </button>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      @if (formOpen()) {
        <section class="card">
          <div class="card-header"><h3>{{ editingId() ? 'Edit user' : 'New user' }}</h3></div>
          <div class="card-body">
            <form class="form-grid" [formGroup]="form" (ngSubmit)="save()">
              <label class="field"><span>Full name *</span><input formControlName="fullName" /></label>
              <label class="field"><span>Email *</span><input formControlName="email" type="email" [readonly]="!!editingId()" /></label>
              <label class="field"><span>Phone</span><input formControlName="phone" /></label>
              <label class="field">
                <span>Role *</span>
                <select formControlName="roleId">
                  <option value="">Select a role</option>
                  @for (r of roles(); track r.id) { <option [value]="r.id">{{ r.name }} ({{ r.permissionCount }} permissions)</option> }
                </select>
              </label>
              @if (editingId()) {
                <label class="row" style="gap:8px"><input type="checkbox" formControlName="isActive" /><span>Active</span></label>
              } @else {
                <label class="field"><span>Password *</span><input formControlName="password" type="password" />
                  <span class="hint">At least 8 characters with letters and digits.</span></label>
              }
              <div class="full row">
                <button class="btn btn-primary" type="submit" [disabled]="saving() || form.invalid">
                  {{ saving() ? 'Saving…' : (editingId() ? 'Update user' : 'Create user') }}
                </button>
                <button class="btn btn-ghost" type="button" (click)="close()">Cancel</button>
              </div>
            </form>
          </div>
        </section>
      }

      <section class="card">
        <div class="card-header">
          <h3>Users</h3>
          <div class="row">
            <input class="btn btn-ghost btn-sm" placeholder="Search name or email" (keyup.enter)="onSearch($event)" />
            <button class="btn btn-ghost btn-sm" (click)="load()">Refresh</button>
          </div>
        </div>
        @if (loading()) {
          <div class="loading">Loading users…</div>
        } @else if (!users().length) {
          <div class="empty">No users found.</div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead><tr><th>Name</th><th>Role</th><th>Last sign-in</th><th>Status</th><th></th></tr></thead>
              <tbody>
                @for (u of users(); track u.id) {
                  <tr>
                    <td><div class="primary">{{ u.fullName }}</div><div class="secondary">{{ u.email }}</div></td>
                    <td>{{ u.roleName }}</td>
                    <td>{{ u.lastLoginAtUtc ? (u.lastLoginAtUtc | date: 'dd MMM yyyy, HH:mm') : 'Never' }}</td>
                    <td>
                      <span class="badge" [class.teal]="u.isActive">{{ u.isActive ? 'Active' : 'Disabled' }}</span>
                      @if (u.isLockedOut) { <span class="badge">Locked</span> }
                    </td>
                    <td>
                      <div class="row" style="gap:6px;justify-content:flex-end">
                        <button class="btn btn-ghost btn-sm" (click)="edit(u)">Edit</button>
                        <button class="btn btn-ghost btn-sm" (click)="resetPassword(u)">Reset password</button>
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
      <section class="card">
        <div class="card-header">
          <h3>Role permissions</h3>
          @if (!editingOtherSchool()) {
            <select class="btn btn-ghost btn-sm" (change)="pickRole($event)">
              <option value="">Choose a role…</option>
              @for (r of roleMatrix(); track r.id) { <option [value]="r.id">{{ r.name }}</option> }
            </select>
          }
        </div>
        <div class="card-body">
          @if (editingOtherSchool()) {
            <div class="empty">Role permissions are edited by each school's own admin.</div>
          } @else if (activeRole()) {
            @let role = activeRole()!;
            <p class="subtitle">Tick what <strong>{{ role.name }}</strong> may do. Users get the change at their next sign-in.</p>
            @for (group of permissionGroups(); track group.module) {
              <div style="margin:10px 0">
                <strong>{{ group.module }}</strong>
                <div class="row" style="flex-wrap:wrap;gap:14px;margin-top:4px">
                  @for (p of group.items; track p.id) {
                    <label class="row" style="gap:6px" [title]="p.description || p.code">
                      <input type="checkbox" [checked]="rolePerms().has(p.code)" (change)="togglePerm(p.code)" />
                      <span>{{ p.description || p.code }}</span>
                    </label>
                  }
                </div>
              </div>
            }
            <button class="btn btn-primary" (click)="saveRole(role)">Save permissions</button>
          } @else {
            <div class="empty">Pick a role to see and change what it can do. Example: give <em>Teacher</em> "Admissions" so teachers can enrol students.</div>
          }
        </div>
      </section>
    </div>
  `
})
export class UsersComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);

  readonly isPlatformAdmin = this.auth.user()?.roleCode === 'SuperAdmin';
  readonly schools = signal<SchoolOption[]>([]);
  readonly schoolId = signal<number | null>(null);
  readonly roles = signal<Role[]>([]);
  readonly users = signal<User[]>([]);
  readonly search = signal('');
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly formOpen = signal(false);
  readonly editingId = signal<number | null>(null);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  readonly roleMatrix = signal<RoleWithPerms[]>([]);
  readonly permissions = signal<Permission[]>([]);
  readonly activeRole = signal<RoleWithPerms | null>(null);
  readonly rolePerms = signal<Set<string>>(new Set());

  editingOtherSchool(): boolean {
    return this.schoolId() !== null && this.schoolId() !== this.auth.user()?.schoolId;
  }

  permissionGroups(): { module: string; items: Permission[] }[] {
    const groups = new Map<string, Permission[]>();
    for (const p of this.permissions()) groups.set(p.module, [...(groups.get(p.module) ?? []), p]);
    return [...groups].map(([module, items]) => ({ module, items }));
  }

  loadMatrix(): void {
    this.api.get<RoleWithPerms[]>('admin/roles').subscribe({
      next: r => this.roleMatrix.set(r.filter(x => x.code !== 'SuperAdmin'))
    });
    this.api.get<Permission[]>('admin/permissions').subscribe({ next: p => this.permissions.set(p), error: (r: unknown) => this.error.set(readError(r)) });
  }

  pickRole(event: Event): void {
    const role = this.roleMatrix().find(r => r.id === Number((event.target as HTMLSelectElement).value)) ?? null;
    this.activeRole.set(role);
    this.rolePerms.set(new Set(role?.permissions ?? []));
  }

  togglePerm(code: string): void {
    const next = new Set(this.rolePerms());
    next.has(code) ? next.delete(code) : next.add(code);
    this.rolePerms.set(next);
  }

  saveRole(role: RoleWithPerms): void {
    const permissionIds = this.permissions().filter(p => this.rolePerms().has(p.code)).map(p => p.id);
    this.api.put<RoleWithPerms>(`admin/roles/${role.id}/permissions`, { permissionIds }).subscribe({
      next: updated => {
        this.message.set(`Permissions saved for ${updated.name}. Users get them at their next sign-in.`);
        this.roleMatrix.update(all => all.map(r => r.id === updated.id ? updated : r));
        this.activeRole.set(updated);
        this.reload();
      },
      error: r => this.error.set(readError(r))
    });
  }

  readonly form = this.fb.nonNullable.group({
    fullName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    phone: [''],
    roleId: ['', Validators.required],
    password: [''],
    isActive: [true]
  });

  ngOnInit(): void {
    const requested = Number(this.route.snapshot.queryParamMap.get('schoolId')) || null;
    this.schoolId.set(this.isPlatformAdmin ? (requested ?? this.auth.user()?.schoolId ?? null) : null);
    if (this.isPlatformAdmin) {
      this.api.get<SchoolOption[]>('platform/schools').subscribe({
        next: s => {
          this.schools.set(s);
          // Default to a real school: the platform workspace has no students or staff.
          if (!requested) {
            const first = s.find(x => x.id !== this.auth.user()?.schoolId);
            if (first && first.id !== this.schoolId()) { this.schoolId.set(first.id); this.reload(); this.loadMatrix(); }
          }
        },
        error: (r: unknown) => this.error.set(readError(r))
      });
    }
    this.reload();
    this.loadMatrix();
  }

  private scope(): Record<string, string | number | undefined> {
    return { schoolId: this.schoolId() ?? undefined };
  }

  reload(): void {
    this.api.get<Role[]>('users/roles', this.scope()).subscribe({
      next: r => this.roles.set(r),
      error: r => this.error.set(readError(r))
    });
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<User[]>('users', { ...this.scope(), search: this.search() || undefined }).subscribe({
      next: u => { this.users.set(u); this.loading.set(false); },
      error: r => { this.error.set(readError(r)); this.loading.set(false); }
    });
  }

  onSchool(event: Event): void {
    this.schoolId.set(Number((event.target as HTMLSelectElement).value));
    this.close();
    this.reload();
  }

  onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value.trim());
    this.load();
  }

  openNew(): void {
    this.editingId.set(null);
    this.form.reset({ fullName: '', email: '', phone: '', roleId: '', password: '', isActive: true });
    this.form.controls.password.setValidators([Validators.required, Validators.minLength(8)]);
    this.form.controls.password.updateValueAndValidity();
    this.formOpen.set(true);
  }

  edit(u: User): void {
    this.editingId.set(u.id);
    this.form.controls.password.clearValidators();
    this.form.reset({ fullName: u.fullName, email: u.email, phone: u.phone ?? '', roleId: String(u.roleId), password: '', isActive: u.isActive });
    this.formOpen.set(true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  close(): void {
    this.formOpen.set(false);
    this.editingId.set(null);
  }

  save(): void {
    if (this.form.invalid || this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.message.set(null);

    const raw = this.form.getRawValue();
    const id = this.editingId();
    const request = id
      ? this.api.put<User>(`users/${id}`, { fullName: raw.fullName, phone: raw.phone || null, roleId: Number(raw.roleId), isActive: raw.isActive })
      : this.api.post<User>('users', {
          schoolId: this.schoolId(), fullName: raw.fullName, email: raw.email, phone: raw.phone || null,
          roleId: Number(raw.roleId), password: raw.password
        });

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.close();
        this.message.set(id ? 'User updated.' : 'User created.');
        this.load();
      },
      error: r => { this.saving.set(false); this.error.set(readError(r)); }
    });
  }

  resetPassword(u: User): void {
    const password = prompt(`New password for ${u.fullName} (at least 8 characters, letters and digits):`);
    if (!password) return;
    this.api.post(`users/${u.id}/reset-password`, { newPassword: password }).subscribe({
      next: () => this.message.set(`Password reset for ${u.fullName}. Their other sessions were signed out.`),
      error: r => this.error.set(readError(r))
    });
  }
}
