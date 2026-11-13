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
        loadComponent: () => import('./pages/directory/directory.component').then(m => m.DirectoryComponent),
        data: {
          title: 'Homework',
          subtitle: 'Assignments, submissions and grading across classes.',
          endpoint: 'homework',
          columns: [
            { key: 'title', label: 'Title' },
            { key: 'subjectName', label: 'Subject' },
            { key: 'teacherName', label: 'Teacher' },
            { key: 'dueAtUtc', label: 'Due' },
            { key: 'submissionCount', label: 'Submissions' },
            { key: 'isOverdue', label: 'Overdue', badge: true }
          ]
        }
      },
      {
        path: 'hr',
        canActivate: [permissionGuard('hr.read')],
        loadComponent: () => import('./pages/directory/directory.component').then(m => m.DirectoryComponent),
        data: {
          title: 'People & HR',
          subtitle: 'Employees, departments, leave and payroll.',
          endpoint: 'hr/employees',
          columns: [
            { key: 'employeeNo', label: 'Employee no.' },
            { key: 'fullName', label: 'Name' },
            { key: 'designation', label: 'Designation' },
            { key: 'department', label: 'Department' },
            { key: 'phone', label: 'Phone' },
            { key: 'status', label: 'Status', badge: true }
          ]
        }
      },
      {
        path: 'transport',
        canActivate: [permissionGuard('transport.manage')],
        loadComponent: () => import('./pages/directory/directory.component').then(m => m.DirectoryComponent),
        data: {
          title: 'Transport',
          subtitle: 'Vehicles, routes, stops and student allocations.',
          endpoint: 'transport/routes',
          columns: [
            { key: 'name', label: 'Route' },
            { key: 'code', label: 'Code' },
            { key: 'startPoint', label: 'Start' },
            { key: 'endPoint', label: 'End' },
            { key: 'stopCount', label: 'Stops' },
            { key: 'monthlyFee', label: 'Monthly fee' }
          ]
        }
      },
      {
        path: 'library',
        canActivate: [permissionGuard('library.manage')],
        loadComponent: () => import('./pages/directory/directory.component').then(m => m.DirectoryComponent),
        data: {
          title: 'Library',
          subtitle: 'Catalogue, issues, returns and fines.',
          endpoint: 'library/books',
          columns: [
            { key: 'title', label: 'Title' },
            { key: 'author', label: 'Author' },
            { key: 'isbn', label: 'ISBN' },
            { key: 'category', label: 'Category' },
            { key: 'availableCopies', label: 'Available' },
            { key: 'totalCopies', label: 'Total' }
          ]
        }
      },
      {
        path: 'inventory',
        canActivate: [permissionGuard('inventory.manage')],
        loadComponent: () => import('./pages/directory/directory.component').then(m => m.DirectoryComponent),
        data: {
          title: 'Inventory',
          subtitle: 'Stock, vendors and purchase orders.',
          endpoint: 'inventory',
          columns: [
            { key: 'name', label: 'Item' },
            { key: 'code', label: 'Code' },
            { key: 'category', label: 'Category' },
            { key: 'quantity', label: 'On hand' },
            { key: 'minimumStock', label: 'Reorder at' },
            { key: 'unitPrice', label: 'Unit price' },
            { key: 'isBelowMinimum', label: 'Low stock', badge: true }
          ]
        }
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
