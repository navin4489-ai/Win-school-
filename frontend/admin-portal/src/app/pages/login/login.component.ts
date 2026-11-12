import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <div class="auth">
      <section class="auth-brand">
        <div>
          <div class="brand-mark">W</div>
          <h1>Run your school with clarity.</h1>
          <p>
            Admissions, attendance, examinations, fees and operations in one secure workspace,
            built for multi-branch schools.
          </p>
          <div class="auth-points">
            <div><span>✓</span> Role-based access for every team</div>
            <div><span>✓</span> Branch-level data isolation</div>
            <div><span>✓</span> Audit trails on every sensitive action</div>
            <div><span>✓</span> WhatsApp, SMS and email notifications</div>
          </div>
        </div>
        <p class="small" style="color:#9fb7cb">Wintech Innovations · School Management System</p>
      </section>

      <section class="auth-panel">
        <div class="auth-card card">
          <div class="card-body">
            <h2>Sign in</h2>
            <p class="muted">Use your school account. Demo accounts are listed below.</p>

            @if (error()) {
              <div class="error" style="margin-top:14px">{{ error() }}</div>
            }

            <form class="form-grid" style="margin-top:18px" [formGroup]="form" (ngSubmit)="submit()">
              <label class="field full">
                <span>Email</span>
                <input type="email" formControlName="email" autocomplete="username" placeholder="you@school.edu" />
              </label>

              <label class="field full">
                <span>Password</span>
                <input type="password" formControlName="password" autocomplete="current-password" placeholder="••••••••" />
              </label>

              <label class="field full">
                <span>School code <span class="muted">(optional)</span></span>
                <input type="text" formControlName="schoolCode" placeholder="e.g. DEMO" />
                <span class="hint">Only needed when your email belongs to more than one school.</span>
              </label>

              <div class="full">
                <button class="btn btn-primary btn-block" type="submit" [disabled]="loading() || form.invalid">
                  {{ loading() ? 'Signing in…' : 'Sign in' }}
                </button>
              </div>
            </form>

            <div class="divider"></div>
            <div class="small muted" style="font-weight:650; text-transform:uppercase; letter-spacing:.08em">Demo accounts</div>
            <div class="demo-list">
              @for (demo of demos; track demo.email) {
                <button type="button" (click)="fill(demo.email)">
                  <span>{{ demo.label }}</span>
                  <span class="muted">{{ demo.email }}</span>
                </button>
              }
            </div>
            <p class="small muted" style="margin-top:10px">Password for every demo account: <strong>Passw0rd!</strong></p>
          </div>
        </div>
      </section>
    </div>
  `
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly demos = [
    { label: 'Super Admin', email: 'superadmin@demo.edu' },
    { label: 'Principal', email: 'principal@demo.edu' },
    { label: 'Admissions Officer', email: 'admissions@demo.edu' },
    { label: 'Accounts Officer', email: 'accounts@demo.edu' },
    { label: 'Teacher', email: 'teacher@demo.edu' },
    { label: 'Parent', email: 'parent@demo.edu' }
  ];

  readonly form = this.fb.nonNullable.group({
    email: ['principal@demo.edu', [Validators.required, Validators.email]],
    password: ['Passw0rd!', [Validators.required]],
    schoolCode: ['']
  });

  fill(email: string): void {
    this.form.patchValue({ email, password: 'Passw0rd!' });
    this.error.set(null);
  }

  submit(): void {
    if (this.form.invalid || this.loading()) return;

    this.loading.set(true);
    this.error.set(null);
    const { email, password, schoolCode } = this.form.getRawValue();

    this.auth.login(email, password, schoolCode).subscribe({
      next: () => {
        this.loading.set(false);
        void this.router.navigate(['/dashboard']);
      },
      error: (response: unknown) => {
        this.loading.set(false);
        this.error.set(readError(response));
      }
    });
  }
}

function readError(response: unknown): string {
  const body = (response as { error?: unknown })?.error;
  if (typeof body === 'string') return body;
  if (body && typeof body === 'object') {
    const problem = body as { detail?: string; title?: string; errors?: Record<string, string[]> };
    if (problem.errors) {
      const first = Object.values(problem.errors)[0];
      if (first?.length) return first[0];
    }
    if (problem.detail) return problem.detail;
    if (problem.title) return problem.title;
  }
  return 'Sign-in failed. Check your email, password and school code.';
}
