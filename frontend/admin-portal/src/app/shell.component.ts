import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { NotificationService } from './core/notification.service';
import { NotificationItem } from './core/models';

interface NavItem {
  label: string;
  route: string;
  icon: string;
  permissions?: string[];
  roles?: string[];
  exceptRoles?: string[];
}
interface NavSection { label: string; items: NavItem[]; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe],
  template: `
    <div class="shell">
      <aside class="sidebar" [class.open]="menuOpen()">
        <div class="brand">
          <img class="brand-mark" src="/logo.svg" alt="Wintech" />
          <div>
            <strong>Wintech</strong>
            <div class="brand-sub">School ERP</div>
          </div>
        </div>

        <div class="school">
          @if (auth.user()?.schoolLogoUrl) {
            <img [src]="auth.user()?.schoolLogoUrl" alt="" style="height:40px;max-width:100%;object-fit:contain;margin-bottom:6px;border-radius:6px;background:#fff;padding:2px" />
          }
          <div class="school-name">{{ auth.user()?.schoolName || 'School workspace' }}</div>
          <div class="school-meta">{{ auth.user()?.role }}</div>
        </div>

        <nav class="nav">
          @for (section of sections; track section.label) {
            @if (hasSection(section)) {
              <div class="nav-label">{{ section.label }}</div>
              @for (item of section.items; track item.route) {
                @if (canSee(item)) {
                  <a class="nav-item" [routerLink]="item.route" routerLinkActive="active" (click)="menuOpen.set(false)">
                    <span class="nav-icon">{{ item.icon }}</span>
                    <span>{{ item.label }}</span>
                  </a>
                }
              }
            }
          }
        </nav>

        <div class="sidebar-footer">
          <div class="avatar">{{ auth.initials() }}</div>
          <div class="who">
            <strong>{{ auth.user()?.fullName }}</strong>
            <span>{{ auth.user()?.email }}</span>
          </div>
          <button class="logout" title="Sign out" aria-label="Sign out" (click)="auth.logout()">⎋</button>
        </div>
      </aside>

      @if (menuOpen()) {
        <button class="backdrop" aria-label="Close menu" (click)="menuOpen.set(false)"></button>
      }

      <div class="main">
        <header class="topbar">
          <button class="menu-btn" aria-label="Open menu" (click)="menuOpen.set(true)">☰</button>
          <div class="topbar-title" [title]="auth.user()?.schoolName || 'Wintech School ERP'">{{ auth.user()?.schoolName || 'Wintech School ERP' }}</div>
          <div class="spacer"></div>

          <button
            class="icon-btn bell"
            [class.active]="bellOpen()"
            title="Notifications"
            aria-label="Notifications"
            (click)="toggleBell()">
            🔔
            @if (notifications.unreadCount() > 0) {
              <span class="badge-count">{{ notifications.unreadCount() > 99 ? '99+' : notifications.unreadCount() }}</span>
            }
          </button>

          <button
            class="icon-btn sound-btn"
            [title]="notifications.soundEnabled() ? 'Mute notification sound' : 'Enable notification sound'"
            [attr.aria-label]="notifications.soundEnabled() ? 'Mute notification sound' : 'Enable notification sound'"
            (click)="notifications.toggleSound()">
            {{ notifications.soundEnabled() ? '🔊' : '🔇' }}
          </button>

          <button class="btn btn-ghost btn-sm signout-btn" (click)="auth.logout()">Sign out</button>
        </header>

        @if (bellOpen()) {
          <div class="bell-panel" (click)="$event.stopPropagation()">
            <div class="bell-panel-head">
              <strong>Notifications</strong>
              <button class="btn btn-ghost btn-sm" (click)="markAllRead()">Mark all read</button>
            </div>

            @if (panelItems().length === 0) {
              <div class="empty small">Nothing new right now.</div>
            } @else {
              <div class="bell-list">
                @for (item of panelItems(); track item.id) {
                  <div class="bell-item" [class.unread]="!item.readAtUtc">
                    <div class="bell-item-title">{{ item.title }}</div>
                    <div class="bell-item-body">{{ item.message }}</div>
                    <div class="bell-item-meta">
                      <span class="badge">{{ item.category }}</span>
                      <span class="small muted">{{ item.createdAtUtc | date:'dd MMM, HH:mm' }}</span>
                      @if (!item.readAtUtc) {
                        <button class="btn btn-ghost btn-sm" (click)="markRead(item)">Mark read</button>
                      }
                    </div>
                  </div>
                }
              </div>
            }

            <a class="bell-panel-foot" routerLink="/notifications" (click)="bellOpen.set(false)">View all notifications</a>
          </div>
        }

        <main class="content">
          <router-outlet />
        </main>
      </div>
    </div>

    <div class="toast-stack" aria-live="polite">
      @for (toast of notifications.toasts(); track toast.id) {
        <div class="toast" role="alert">
          <div class="toast-icon">🔔</div>
          <div class="toast-body">
            <div class="toast-title">{{ toast.title }}</div>
            <div class="toast-message">{{ toast.message }}</div>
          </div>
          <button class="toast-close" aria-label="Dismiss" (click)="notifications.dismiss(toast.id)">✕</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .shell { min-height: 100vh; display: grid; grid-template-columns: 250px minmax(0, 1fr); }
    .sidebar { background: linear-gradient(180deg, var(--navy-900), var(--navy-800)); color: #eaf2f9; display: flex; flex-direction: column; position: sticky; top: 0; height: 100vh; overflow-y: auto; z-index: 30; }
    .brand { display: flex; align-items: center; gap: 11px; padding: 20px 18px 14px; }
    .brand-mark { width: 38px; height: 38px; border-radius: 10px; display: block; }
    .brand strong { font-size: 16px; letter-spacing: -0.01em; }
    .brand-sub { font-size: 11px; color: #9fb7cb; letter-spacing: 0.14em; text-transform: uppercase; }
    .school { margin: 4px 14px 10px; padding: 10px 12px; border: 1px solid rgba(255,255,255,0.08); background: rgba(255,255,255,0.04); border-radius: 10px; }
    .school-name { font-size: 12px; font-weight: 600; color: #f2f7fb; }
    .school-meta { font-size: 11px; color: #9fb7cb; margin-top: 2px; }
    .nav { padding: 4px 10px 18px; display: grid; gap: 2px; }
    .nav-label { font-size: 10px; text-transform: uppercase; letter-spacing: 0.14em; color: #7f9ab1; margin: 14px 8px 5px; font-weight: 700; }
    .nav-item { display: flex; align-items: center; gap: 10px; padding: 8px 10px; color: #cfe0ee; border-radius: 8px; font-size: 13px; font-weight: 550; text-decoration: none; }
    .nav-item:hover { background: rgba(255,255,255,0.07); color: #fff; text-decoration: none; }
    .nav-item.active { background: rgba(244,180,26,0.16); color: #ffd775; box-shadow: inset 3px 0 0 #f4b41a; }
    .nav-icon { width: 20px; text-align: center; opacity: 0.9; }
    .sidebar-footer { margin-top: auto; border-top: 1px solid rgba(255,255,255,0.08); padding: 13px 14px; display: flex; align-items: center; gap: 9px; }
    .who { display: grid; min-width: 0; flex: 1; }
    .who strong { font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .who span { font-size: 10px; color: #9fb7cb; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .logout { border: 0; background: transparent; color: #cfe0ee; font-size: 18px; padding: 4px; border-radius: 6px; }
    .logout:hover { background: rgba(255,255,255,0.08); }
    .main { min-width: 0; display: flex; flex-direction: column; }
    .topbar { height: 60px; display: flex; align-items: center; gap: 12px; padding: 0 24px; background: rgba(255,255,255,0.88); backdrop-filter: blur(8px); border-bottom: 1px solid var(--line); position: sticky; top: 0; z-index: 20; }
    .topbar-title { font-weight: 650; color: var(--navy-800); font-size: 14.5px; min-width: 0; flex: 0 1 auto; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .session { font-size: 11px; color: var(--ink-500); background: #eef4f9; padding: 4px 9px; border-radius: 999px; }
    .menu-btn { display: none; border: 1px solid var(--line); background: #fff; border-radius: 8px; width: 36px; height: 36px; font-size: 17px; }
    .icon-btn { position: relative; flex: none; display: grid; place-items: center; width: 36px; height: 36px; border-radius: 9px; border: 1px solid var(--line); background: #fff; text-decoration: none; }
    .icon-btn.active { background: var(--teal-100); border-color: var(--teal-500); }
    .badge-count { position: absolute; top: -6px; right: -6px; min-width: 18px; height: 18px; padding: 0 5px; border-radius: 999px; background: var(--red-500); color: #fff; font-size: 10px; font-weight: 700; display: grid; place-items: center; }
    .bell-panel { position: absolute; top: 68px; right: 24px; width: min(380px, calc(100vw - 28px)); background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius); box-shadow: var(--shadow); z-index: 40; overflow: hidden; }
    .bell-panel-head { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 12px 14px; border-bottom: 1px solid var(--line); }
    .bell-list { max-height: 340px; overflow-y: auto; }
    .bell-item { padding: 11px 14px; border-bottom: 1px solid var(--line); }
    .bell-item.unread { background: #f7fcfe; }
    .bell-item-title { font-weight: 650; font-size: 13px; }
    .bell-item-body { color: var(--ink-500); font-size: 12px; margin-top: 2px; }
    .bell-item-meta { display: flex; align-items: center; gap: 8px; margin-top: 7px; }
    .bell-panel-foot { display: block; padding: 11px 14px; text-align: center; font-size: 12px; font-weight: 650; background: #fbfdff; }
    .content { padding: 24px; max-width: 1440px; width: 100%; margin: 0 auto; }
    .backdrop { display: none; }
    .toast-stack { position: fixed; right: 20px; bottom: 20px; display: grid; gap: 10px; z-index: 60; width: min(360px, calc(100vw - 28px)); }
    .toast { display: flex; align-items: flex-start; gap: 10px; background: var(--surface); border: 1px solid var(--line); border-left: 4px solid var(--teal-500); border-radius: var(--radius-sm); box-shadow: var(--shadow); padding: 12px 13px; animation: toast-in .22s ease-out; }
    .toast-icon { font-size: 17px; }
    .toast-body { flex: 1; min-width: 0; }
    .toast-title { font-weight: 650; font-size: 13px; }
    .toast-message { color: var(--ink-500); font-size: 12px; margin-top: 2px; }
    .toast-close { border: 0; background: transparent; color: var(--ink-500); font-size: 13px; padding: 2px; }
    @keyframes toast-in { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: none; } }
    @media (max-width: 900px) {
      .shell { grid-template-columns: 1fr; }
      .sidebar { position: fixed; left: 0; top: 0; width: 260px; transform: translateX(-100%); transition: transform 0.2s ease; }
      .sidebar.open { transform: translateX(0); }
      .menu-btn { display: grid; place-items: center; }
      .backdrop { display: block; position: fixed; inset: 0; background: rgba(6, 20, 35, 0.45); border: 0; z-index: 25; }
      .content { padding: 18px 14px; }
      .topbar { padding: 0 14px; }
      .session { display: none; }
      .bell-panel { right: 14px; position: fixed; top: 64px; }
    }
    @media (max-width: 600px) {
      .sound-btn, .signout-btn { display: none; }
      .topbar { gap: 10px; }
      .content { padding: 16px 12px 28px; }
    }
  `]
})
export class ShellComponent implements OnInit, OnDestroy {
  readonly auth = inject(AuthService);
  readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);

  readonly menuOpen = signal(false);
  readonly bellOpen = signal(false);
  readonly panelItems = signal<NotificationItem[]>([]);

  readonly sections: NavSection[] = [
    {
      label: 'Overview',
      items: [
        { label: 'Dashboard', route: '/dashboard', icon: '◫' },
        { label: 'Notifications', route: '/notifications', icon: '◔' }
      ]
    },
    {
      label: 'Admissions & students',
      items: [
        { label: 'Admissions', route: '/admissions', icon: '✎', permissions: ['admissions.manage'] },
        { label: 'Students', route: '/students', icon: '👥', permissions: ['students.read'] }
      ]
    },
    {
      label: 'Academics',
      items: [
        { label: 'My attendance', route: '/my-attendance', icon: '⏱', permissions: ['attendance.mark'], exceptRoles: ['Parent'] },
        { label: 'My children attendance', route: '/parent-attendance', icon: '✓', roles: ['Parent'] },
        { label: 'Attendance', route: '/attendance', icon: '✓', permissions: ['attendance.read'], exceptRoles: ['Parent'] },
        { label: 'Class & teachers', route: '/assignments', icon: '⚙', permissions: ['settings.manage'] },
        { label: 'Examinations', route: '/exams', icon: '▤', permissions: ['exams.read'] },
        { label: 'Homework', route: '/homework', icon: '✐' }
      ]
    },
    {
      label: 'Finance',
      items: [
        { label: 'Fees & payments', route: '/fees', icon: '₹', permissions: ['fees.read'] },
        { label: 'Reports', route: '/reports', icon: '▦', permissions: ['reports.read'] }
      ]
    },
    {
      label: 'Operations',
      items: [
        { label: 'People & HR', route: '/hr', icon: '◈', permissions: ['hr.read'] },
        { label: 'Transport', route: '/transport', icon: '⛟', permissions: ['transport.manage'] },
        { label: 'Library', route: '/library', icon: '▥', permissions: ['library.manage'] },
        { label: 'Inventory', route: '/inventory', icon: '▣', permissions: ['inventory.manage'] },
        { label: 'Classes & subjects', route: '/settings', icon: '⚙', permissions: ['settings.manage'] }
      ]
    },
    {
      label: 'Administration',
      items: [
        { label: 'Users & roles', route: '/users', icon: '☺', permissions: ['users.manage'] },
        // Not a real permission: only the SuperAdmin passes, because hasPermission lets that role through.
        { label: 'Schools', route: '/schools', icon: '⌂', permissions: ['platform.schools'] }
      ]
    }
  ];

  ngOnInit(): void {
    this.notifications.connect();
    this.notifications.refreshCount();
  }

  ngOnDestroy(): void {
    this.notifications.disconnect();
  }

  toggleBell(): void {
    this.bellOpen.update(open => !open);
    if (this.bellOpen()) void this.loadPanel();
  }

  markRead(item: NotificationItem): void {
    this.notifications.markRead(item);
    this.panelItems.update(items => [...items]);
    void fetch(`/api/notifications/${item.id}/read`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${localStorage.getItem('school_erp_access') ?? ''}` }
    });
  }

  markAllRead(): void {
    this.panelItems.update(items => items.map(item => ({ ...item, readAtUtc: item.readAtUtc ?? new Date().toISOString() })));
    this.notifications.unreadCount.set(0);
    void fetch('/api/notifications/read-all', {
      method: 'POST',
      headers: { Authorization: `Bearer ${localStorage.getItem('school_erp_access') ?? ''}` }
    });
  }

  canSee(item: NavItem): boolean {
    if (item.roles?.length && !item.roles.some(role => this.auth.hasRole(role))) return false;
    if (item.exceptRoles?.some(role => this.auth.hasRole(role))) return false;
    // Check-in needs a staff (employee) record; the platform super admin has none.
    if (item.route === '/my-attendance' && !this.auth.user()?.employeeId) return false;
    return this.auth.hasAny(item.permissions ?? []);
  }

  hasSection(section: NavSection): boolean {
    return section.items.some(item => this.canSee(item));
  }

  private async loadPanel(): Promise<void> {
    try {
      const response = await fetch('/api/notifications?unreadOnly=false', {
        headers: { Authorization: `Bearer ${localStorage.getItem('school_erp_access') ?? ''}` }
      });

      if (!response.ok) return;
      const items = (await response.json()) as NotificationItem[];
      this.panelItems.set(items.slice(0, 8));
    } catch {
      // Leave the panel empty rather than showing an error in the header.
    }
  }
}
