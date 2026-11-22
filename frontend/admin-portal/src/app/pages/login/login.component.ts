import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <div class="login">
      <section class="hero">
        <div class="hero-top">
          <img class="logo" src="/logo.svg" alt="Wintech" />
          <div>
            <strong>Wintech</strong>
            <div class="logo-sub">School ERP</div>
          </div>
        </div>

        <div class="hero-copy">
          <h1>One workspace for<br /><span>every school you run.</span></h1>
          <p>Admissions, attendance, exams, fees, HR and transport, with each school's data kept fully separate.</p>

          <div class="features">
            @for (f of features; track f.title) {
              <div class="feature">
                <div class="feature-icon">{{ f.icon }}</div>
                <div>
                  <div class="feature-title">{{ f.title }}</div>
                  <div class="feature-text">{{ f.text }}</div>
                </div>
              </div>
            }
          </div>
        </div>

        <div class="hero-foot">© {{ year }} Wintech Innovations</div>
        <div class="orb orb-1"></div>
        <div class="orb orb-2"></div>
      </section>

      <section class="panel">
        <div class="card-login">
          <div class="mobile-brand"><img class="logo" src="/logo.svg" alt="Wintech" /><strong>Wintech School ERP</strong></div>

          <h2>Welcome back</h2>
          <p class="lead">Staff: use your email. Parents: use your child's admission number.</p>

          @if (error()) {
            <div class="alert" role="alert">{{ error() }}</div>
          }

          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <label class="input-group">
              <span class="label">Email or admission number</span>
              <div class="input-wrap" [class.invalid]="touchedInvalid('email')">
                <span class="icon">✉</span>
                <input type="text" formControlName="email" autocomplete="username" placeholder="you@school.edu or STU-2026-00001" />
              </div>
            </label>

            <label class="input-group">
              <span class="label">Password</span>
              <div class="input-wrap" [class.invalid]="touchedInvalid('password')">
                <span class="icon">🔒</span>
                <input [type]="showPassword() ? 'text' : 'password'" formControlName="password"
                       autocomplete="current-password" placeholder="Your password" />
                <button type="button" class="toggle" (click)="showPassword.set(!showPassword())"
                        [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'">
                  {{ showPassword() ? 'Hide' : 'Show' }}
                </button>
              </div>
            </label>

            <button class="submit" type="submit" [disabled]="loading() || form.invalid">
              @if (loading()) { <span class="spinner"></span> Signing in… } @else { Sign in → }
            </button>
          </form>

          <div class="help">
            <button type="button" class="link" (click)="helpOpen.set(!helpOpen())">Forgot password?</button>
            @if (helpOpen()) {
              <p class="help-text">
                Contact your school administrator. They can reset your password from
                <strong>Users &amp; roles</strong>.
              </p>
            }
          </div>

          <p class="secure">🔒 Never share your password. School staff will never ask you for it.</p>
        </div>
      </section>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .login { min-height: 100vh; display: grid; grid-template-columns: 1.1fr 1fr; background: #f4f7fb; }

    .hero { position: relative; overflow: hidden; display: flex; flex-direction: column; justify-content: space-between;
      padding: 40px 56px; color: #fff; background: linear-gradient(145deg, #0b1f3a 0%, #12345c 55%, #0f8a86 130%); }
    .hero-top { display: flex; align-items: center; gap: 12px; position: relative; z-index: 1; }
    .logo { width: 42px; height: 42px; border-radius: 12px; display: block; box-shadow: 0 6px 18px rgba(0, 0, 0, .25); }
    .logo-sub { font-size: 11px; letter-spacing: .16em; text-transform: uppercase; color: #9fb7cb; }
    .hero-copy { position: relative; z-index: 1; max-width: 520px; }
    .hero-copy h1 { font-size: 40px; line-height: 1.15; letter-spacing: -.02em; margin: 0 0 16px; }
    .hero-copy h1 span { color: #7fe0d8; }
    .hero-copy p { color: #c9d8e6; font-size: 16px; line-height: 1.6; margin: 0 0 32px; }
    .features { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
    .feature { display: flex; gap: 12px; padding: 14px; border-radius: 14px; background: rgba(255,255,255,.06);
      border: 1px solid rgba(255,255,255,.10); backdrop-filter: blur(6px); }
    .feature-icon { width: 34px; height: 34px; flex: none; border-radius: 10px; display: grid; place-items: center;
      background: rgba(127, 224, 216, .15); color: #7fe0d8; font-size: 16px; }
    .feature-title { font-weight: 700; font-size: 14px; }
    .feature-text { font-size: 12.5px; color: #a9bfd3; margin-top: 2px; line-height: 1.4; }
    .hero-foot { font-size: 12px; color: #8aa4bb; position: relative; z-index: 1; }
    .orb { position: absolute; border-radius: 50%; filter: blur(10px); opacity: .35; }
    .orb-1 { width: 380px; height: 380px; right: -120px; top: -120px; background: radial-gradient(circle, #0f8a86, transparent 70%); }
    .orb-2 { width: 300px; height: 300px; left: -100px; bottom: -120px; background: radial-gradient(circle, #f3b33d, transparent 70%); }

    .panel { display: grid; place-items: center; padding: 32px 24px; min-width: 0; }
    .card-login { width: 100%; max-width: 420px; background: #fff; border-radius: 20px; padding: 36px 32px;
      box-shadow: 0 20px 50px rgba(15, 42, 74, .10); border: 1px solid #e6edf4; }
    .mobile-brand { display: none; }
    h2 { margin: 0; font-size: 26px; color: #0b1f3a; letter-spacing: -.01em; }
    .lead { margin: 6px 0 22px; color: #5b6b7c; font-size: 14px; }

    .alert { margin-bottom: 16px; padding: 11px 14px; border-radius: 10px; background: #fdecea; color: #a93226;
      border: 1px solid #f5c6c0; font-size: 13.5px; }

    .input-group { display: block; margin-bottom: 16px; }
    .label { display: block; font-size: 12.5px; font-weight: 700; color: #2b3b4c; margin-bottom: 6px; }
    .input-wrap { display: flex; align-items: center; border: 1.5px solid #dbe3ec; border-radius: 12px; background: #fbfcfe;
      transition: border-color .15s, box-shadow .15s, background .15s; }
    .input-wrap:focus-within { border-color: #0f8a86; background: #fff; box-shadow: 0 0 0 4px rgba(15, 138, 134, .12); }
    .input-wrap.invalid { border-color: #e0a19a; }
    .icon { width: 40px; text-align: center; color: #8a9aab; font-size: 14px; flex: none; }
    .input-wrap input { flex: 1; min-width: 0; border: 0; outline: 0; background: transparent; padding: 12px 12px 12px 0;
      font-size: 14.5px; color: #15202b; }
    .toggle { border: 0; background: transparent; color: #0f8a86; font-weight: 700; font-size: 12.5px; padding: 0 14px; cursor: pointer; }
    .hint { display: block; margin-top: 5px; font-size: 11.5px; color: #7b8a99; }

    .submit { width: 100%; margin-top: 6px; padding: 13px; border: 0; border-radius: 12px; cursor: pointer;
      font-weight: 700; font-size: 15px; color: #fff; background: linear-gradient(135deg, #0f8a86, #0c6f6c);
      box-shadow: 0 8px 20px rgba(15, 138, 134, .28); display: inline-flex; justify-content: center; align-items: center; gap: 8px;
      transition: transform .12s, box-shadow .12s, opacity .12s; }
    .submit:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 12px 24px rgba(15, 138, 134, .32); }
    .submit:disabled { opacity: .6; cursor: not-allowed; box-shadow: none; }
    .spinner { width: 15px; height: 15px; border-radius: 50%; border: 2px solid rgba(255,255,255,.4); border-top-color: #fff;
      animation: spin .8s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }

    .help { margin-top: 18px; text-align: center; }
    .link { border: 0; background: transparent; color: #0f8a86; font-weight: 700; font-size: 13px; cursor: pointer; padding: 0; }
    .link:hover { text-decoration: underline; }
    .help-text { margin: 10px 0 0; padding: 11px 13px; border-radius: 10px; background: #f1f8f8; color: #3c4d5e;
      font-size: 13px; line-height: 1.5; text-align: left; }
    .secure { margin: 22px 0 0; padding-top: 14px; border-top: 1px solid #edf1f5; font-size: 11.5px; color: #8a9aab; text-align: center; }

    @media (max-width: 900px) {
      .login { grid-template-columns: minmax(0, 1fr); }
      .hero { display: none; }
      .panel { min-height: 100vh; background: linear-gradient(180deg, #0b1f3a 0, #12345c 220px, #f4f7fb 220px); }
      .mobile-brand { display: flex; align-items: center; gap: 10px; margin-bottom: 22px; color: #0b1f3a; }
      .mobile-brand .logo { width: 34px; height: 34px; font-size: 16px; }
    }
    @media (max-width: 420px) {
      .card-login { padding: 28px 20px; }
    }
  `]
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPassword = signal(false);
  readonly helpOpen = signal(false);
  readonly year = new Date().getFullYear();

  readonly features = [
    { icon: '⌂', title: 'Multi-school', text: 'Each school sees only its own data.' },
    { icon: '✓', title: 'Attendance', text: 'Staff check-in with photo and location.' },
    { icon: '₹', title: 'Online fees', text: 'Payments, receipts and late fees.' },
    { icon: '◔', title: 'Live alerts', text: 'Instant notifications for every role.' }
  ];

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required]],
    password: ['', [Validators.required]]
  });

  touchedInvalid(name: 'email' | 'password'): boolean {
    const control = this.form.controls[name];
    return control.touched && control.invalid;
  }

  submit(): void {
    if (this.form.invalid || this.loading()) return;

    this.loading.set(true);
    this.error.set(null);
    const { email, password } = this.form.getRawValue();

    this.auth.login(email, password).subscribe({
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
  return 'Sign-in failed. Check your email and password.';
}
