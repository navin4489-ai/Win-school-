import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';

interface NavItem { label: string; route: string; icon: string; permissions?: string[]; }
interface NavSection { label: string; items: NavItem[]; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <aside class="sidebar" [class.open]="menuOpen()">
        <div class="brand">
          <div class="brand-mark">W</div>
          <div>
            <strong>Wintech</strong>
            <div class="brand-sub">School ERP</div>
          </div>
        </div>

        <div class="school">
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
          <button class="logout" title="Sign out" (click)="auth.logout()">⎋</button>
        </div>
      </aside>

      @if (menuOpen()) {
        <button class="backdrop" aria-label="Close menu" (click)="menuOpen.set(false)"></button>
      }

      <div class="main">
        <header class="topbar">
          <button class="menu-btn" aria-label="Open menu" (click)="menuOpen.set(true)">☰</button>
          <div class="topbar-title">School Management System</div>
          <div class="spacer"></div>
          <span class="session">Demo environment</span>
          <a class="icon-btn" routerLink="/notifications" title="Notifications">🔔</a>
          <button class="btn btn-ghost btn-sm" (click)="auth.logout()">Sign out</button>
        </header>

        <main class="content">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
  styles: [`
    .shell { min-height: 100vh; display: grid; grid-template-columns: 250px minmax(0, 1fr); }
    .sidebar { background: linear-gradient(180deg, var(--navy-900), var(--navy-800)); color: #eaf2f9; display: flex; flex-direction: column; position: sticky; top: 0; height: 100vh; overflow-y: auto; z-index: 30; }
    .brand { display: flex; align-items: center; gap: 11px; padding: 20px 18px 14px; }
    .brand-mark { width: 38px; height: 38px; border-radius: 11px; background: var(--gold-500); color: var(--navy-900); display: grid; place-items: center; font-weight: 800; font-size: 18px; }
    .brand strong { font-size: 16px; letter-spacing: -0.01em; }
    .brand-sub { font-size: 11px; color: #9fb7cb; letter-spacing: 0.14em; text-transform: uppercase; }
    .school { margin: 4px 14px 10px; padding: 10px 12px; border: 1px solid rgba(255,255,255,0.08); background: rgba(255,255,255,0.04); border-radius: 10px; }
    .school-name { font-size: 12px; font-weight: 600; color: #f2f7fb; }
    .school-meta { font-size: 11px; color: #9fb7cb; margin-top: 2px; }
    .nav { padding: 4px 10px 18px; display: grid; gap: 2px; }
    .nav-label { font-size: 10px; text-transform: uppercase; letter-spacing: 0.14em; color: #7f9ab1; margin: 14px 8px 5px; font-weight: 700; }
    .nav-item { display: flex; align-items: center; gap: 10px; padding: 8px 10px; color: #cfe0ee; border-radius: 8px; font-size: 13px; font-weight: 550; text-decoration: none; }
    .nav-item:hover { background: rgba(255,255,255,0.07); color: #fff; text-decoration: none; }
    .nav-item.active { background: rgba(244,180,26,0.16); color: #ffd775; }
    .nav-icon { width: 20px; text-align: center; opacity: 0.9; }
    .sidebar-footer { margin-top: auto; border-top: 1px solid rgba(255,255,255,0.08); padding: 13px 14px; display: flex; align-items: center; gap: 9px; }
    .who { display: grid; min-width: 0; flex: 1; }
    .who strong { font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .who span { font-size: 10px; color: #9fb7cb; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .logout { border: 0; background: transparent; color: #cfe0ee; font-size: 18px; padding: 4px; border-radius: 6px; }
    .logout:hover { background: rgba(255,255,255,0.08); }
    .main { min-width: 0; display: flex; flex-direction: column; }
    .topbar { height: 62px; display: flex; align-items: center; gap: 12px; padding: 0 24px; background: rgba(255,255,255,0.88); backdrop-filter: blur(8px); border-bottom: 1px solid var(--line); position: sticky; top: 0; z-index: 20; }
    .topbar-title { font-weight: 650; color: var(--navy-800); font-size: 14px; }
    .session { font-size: 11px; color: var(--ink-500); background: #eef4f9; padding: 4px 9px; border-radius: 999px; }
    .menu-btn { display: none; border: 1px solid var(--line); background: #fff; border-radius: 8px; width: 36px; height: 36px; font-size: 17px; }
    .icon-btn { display: grid; place-items: center; width: 36px; height: 36px; border-radius: 9px; border: 1px solid var(--line); background: #fff; text-decoration: none; }
    .content { padding: 24px; max-width: 1440px; width: 100%; margin: 0 auto; }
    .backdrop { display: none; }
    @media (max-width: 900px) {
      .shell { grid-template-columns: 1fr; }
      .sidebar { position: fixed; left: 0; top: 0; width: 260px; transform: translateX(-100%); transition: transform 0.2s ease; }
      .sidebar.open { transform: translateX(0); }
      .menu-btn { display: grid; place-items: center; }
      .backdrop { display: block; position: fixed; inset: 0; background: rgba(6, 20, 35, 0.45); border: 0; z-index: 25; }
      .content { padding: 18px 14px; }
      .topbar { padding: 0 14px; }
      .session { display: none; }
    }
  `]
})
export class ShellComponent {
  readonly auth = inject(AuthService);
  readonly menuOpen = signal(false);

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
        { label: 'Attendance', route: '/attendance', icon: '✓', permissions: ['attendance.read'] },
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
        { label: 'Settings', route: '/settings', icon: '⚙', permissions: ['settings.manage'] }
      ]
    }
  ];

  canSee(item: NavItem): boolean {
    return this.auth.hasAny(item.permissions ?? []);
  }

  hasSection(section: NavSection): boolean {
    return section.items.some(item => this.canSee(item));
  }
}
