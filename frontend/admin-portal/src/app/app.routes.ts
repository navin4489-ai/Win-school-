import { Routes } from '@angular/router';
import { ShellComponent } from './shell.component';
import { authGuard, guestGuard, permissionGuard } from './core/guards';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () => import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent)
      },
      {
        path: 'admissions',
        canActivate: [permissionGuard('admissions.manage')],
        loadComponent: () => import('./pages/admissions/admissions.component').then(m => m.AdmissionsComponent)
      },
      {
        path: 'students',
        canActivate: [permissionGuard('students.read')],
        loadComponent: () => import('./pages/students/students.component').then(m => m.StudentsComponent)
      },
      {
        path: 'students/:id',
        canActivate: [permissionGuard('students.read')],
        loadComponent: () => import('./pages/students/student-detail.component').then(m => m.StudentDetailComponent)
      },
      {
        path: 'fees/setup',
        canActivate: [permissionGuard('fees.collect')],
        loadComponent: () => import('./pages/fees/fee-setup.component').then(m => m.FeeSetupComponent)
      },
      {
        path: 'fees',
        canActivate: [permissionGuard('fees.read')],
        loadComponent: () => import('./pages/fees/fees.component').then(m => m.FeesComponent)
      },
      {
        path: 'attendance',
        canActivate: [permissionGuard('attendance.read')],
        loadComponent: () => import('./pages/attendance/attendance.component').then(m => m.AttendanceComponent)
      },
      {
        path: 'my-attendance',
        canActivate: [permissionGuard('attendance.mark')],
        loadComponent: () => import('./pages/attendance/my-attendance.component').then(m => m.MyAttendanceComponent)
      },
      {
        path: 'assignments',
        canActivate: [permissionGuard('settings.manage')],
        loadComponent: () => import('./pages/assignments/assignments.component').then(m => m.AssignmentsComponent)
      },
      {
        path: 'exams',
        canActivate: [permissionGuard('exams.read')],
        loadComponent: () => import('./pages/exams/exams.component').then(m => m.ExamsComponent)
      },
      {
        path: 'reports',
        canActivate: [permissionGuard('reports.read')],
        loadComponent: () => import('./pages/reports/reports.component').then(m => m.ReportsComponent)
      },
      {
        path: 'notifications',
        loadComponent: () => import('./pages/notifications/notifications.component').then(m => m.NotificationsComponent)
      },
      {
        path: 'homework',
        loadComponent: () => import('./pages/homework/homework.component').then(m => m.HomeworkComponent)
      },
      {
        path: 'hr',
        canActivate: [permissionGuard('hr.read')],
        loadComponent: () => import('./pages/operations/hr.component').then(m => m.HrComponent)
      },
      {
        path: 'transport',
        canActivate: [permissionGuard('transport.manage')],
        loadComponent: () => import('./pages/operations/transport.component').then(m => m.TransportComponent)
      },
      {
        path: 'library',
        canActivate: [permissionGuard('library.manage')],
        loadComponent: () => import('./pages/operations/library.component').then(m => m.LibraryComponent)
      },
      {
        path: 'inventory',
        canActivate: [permissionGuard('inventory.manage')],
        loadComponent: () => import('./pages/operations/inventory.component').then(m => m.InventoryComponent)
      },
      {
        path: 'settings',
        canActivate: [permissionGuard('settings.manage')],
        loadComponent: () => import('./pages/settings/classes.component').then(m => m.ClassesComponent)
      },
      {
        path: 'schools',
        canActivate: [permissionGuard('platform.schools')],
        loadComponent: () => import('./pages/platform/schools.component').then(m => m.SchoolsComponent)
      },
      {
        path: 'users',
        canActivate: [permissionGuard('users.manage')],
        loadComponent: () => import('./pages/platform/users.component').then(m => m.UsersComponent)
      },
      {
        path: 'forbidden',
        loadComponent: () => import('./pages/forbidden/forbidden.component').then(m => m.ForbiddenComponent)
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
