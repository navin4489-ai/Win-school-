import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { AuthResponse, UserProfile } from './models';

const ACCESS_KEY = 'school_erp_access';
const REFRESH_KEY = 'school_erp_refresh';
const USER_KEY = 'school_erp_user';
const EXPIRES_KEY = 'school_erp_expires';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly user = signal<UserProfile | null>(this.readUser());
  readonly accessToken = signal<string | null>(localStorage.getItem(ACCESS_KEY));
  readonly isLoggedIn = computed(() => !!this.accessToken());
  readonly permissions = computed(() => this.user()?.permissions ?? []);

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/login', { email, password })
      .pipe(tap(response => this.persist(response)));
  }

  refresh(): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/refresh', { refreshToken: this.getRefreshToken() })
      .pipe(tap(response => this.persist(response)));
  }

  logout(): void {
    const refreshToken = this.getRefreshToken();
    if (refreshToken) {
      this.http.post('/api/auth/logout', { refreshToken }).subscribe({ error: () => undefined });
    }
    this.clearSession();
    void this.router.navigate(['/login']);
  }

  clearSession(): void {
    localStorage.removeItem(ACCESS_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
    localStorage.removeItem(EXPIRES_KEY);
    this.accessToken.set(null);
    this.user.set(null);
  }

  getRefreshToken(): string | null {
    return localStorage.getItem(REFRESH_KEY);
  }

  has(permission: string): boolean {
    const user = this.user();
    return !!user && (user.roleCode === 'SuperAdmin' || user.permissions.includes(permission));
  }

  hasAny(permissions: string[]): boolean {
    return permissions.length === 0 || permissions.some(permission => this.has(permission));
  }

  initials(): string {
    const name = this.user()?.fullName ?? '';
    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0]?.toUpperCase() ?? '')
      .join('');
  }

  private persist(response: AuthResponse): void {
    localStorage.setItem(ACCESS_KEY, response.accessToken);
    localStorage.setItem(REFRESH_KEY, response.refreshToken);
    localStorage.setItem(USER_KEY, JSON.stringify(response.user));
    localStorage.setItem(EXPIRES_KEY, response.expiresAtUtc);
    this.accessToken.set(response.accessToken);
    this.user.set(response.user);
  }

  private readUser(): UserProfile | null {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as UserProfile;
    } catch {
      return null;
    }
  }
}
