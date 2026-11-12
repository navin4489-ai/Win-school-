import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ClassRoom, PagedResult, StudentListItem } from '../../core/models';

@Component({
  selector: 'app-students',
  standalone: true,
  imports: [RouterLink, DatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Students</div>
          <h1>Student directory</h1>
          <p class="subtitle">Search enrolled students, review profiles and jump into fees, attendance or results.</p>
        </div>
        <div class="toolbar">
          <span class="badge teal">{{ total() }} students</span>
          <a class="btn btn-primary" routerLink="/admissions">+ Enrol student</a>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }

      <section class="card">
        <div class="card-header">
          <div class="filters" style="flex:1">
            <input placeholder="Search name, admission no. or phone" [value]="search()" (input)="setSearch($event)" />
            <select [value]="classId()" (change)="setClass($event)">
              <option value="">All classes</option>
              @for (item of classes(); track item.id) {
                <option [value]="item.id">{{ item.name }}</option>
              }
            </select>
            <select [value]="status()" (change)="setStatus($event)">
              <option value="Active">Active</option>
              <option value="Withdrawn">Withdrawn</option>
              <option value="Alumni">Alumni</option>
              <option value="Transferred">Transferred</option>
            </select>
            <button class="btn btn-ghost btn-sm" (click)="load()">Refresh</button>
          </div>
        </div>

        @if (loading()) {
          <div class="loading">Loading students…</div>
        } @else if (!rows().length) {
          <div class="empty">No students match these filters.</div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Student</th>
                  <th>Class</th>
                  <th>Roll no.</th>
                  <th>Contact</th>
                  <th>Admitted</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (row of rows(); track row.id) {
                  <tr>
                    <td>
                      <div class="row" style="gap:9px;flex-wrap:nowrap">
                        <div class="avatar">{{ initials(row.fullName) }}</div>
                        <div>
                          <div class="primary">{{ row.fullName }}</div>
                          <div class="secondary">{{ row.admissionNo }}</div>
                        </div>
                      </div>
                    </td>
                    <td>{{ row.className }} · {{ row.sectionName }}</td>
                    <td>{{ row.rollNo || '—' }}</td>
                    <td>{{ row.phone || '—' }}</td>
                    <td class="small muted">{{ row.admissionDate | date:'dd MMM yyyy' }}</td>
                    <td><span [class]="'badge ' + (row.status === 'Active' ? 'green' : 'amber')">{{ row.status }}</span></td>
                    <td><a class="btn btn-ghost btn-sm" [routerLink]="['/students', row.id]">Open</a></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="card-footer pagination">
            <span class="small muted">Page {{ page() }} · {{ total() }} total</span>
            <div class="row">
              <button class="btn btn-ghost btn-sm" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</button>
              <button class="btn btn-ghost btn-sm" [disabled]="rows().length < pageSize" (click)="go(page() + 1)">Next</button>
            </div>
          </div>
        }
      </section>
    </div>
  `
})
export class StudentsComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly rows = signal<StudentListItem[]>([]);
  readonly classes = signal<ClassRoom[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly search = signal('');
  readonly classId = signal('');
  readonly status = signal('Active');
  readonly pageSize = 20;

  ngOnInit(): void {
    this.api.get<ClassRoom[]>('admin/classes').subscribe({
      next: classes => this.classes.set(classes),
      error: () => undefined
    });
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api
      .get<PagedResult<StudentListItem>>('students', {
        page: this.page(),
        pageSize: this.pageSize,
        search: this.search(),
        classId: this.classId(),
        status: this.status()
      })
      .subscribe({
        next: result => {
          this.rows.set(result.items);
          this.total.set(result.totalCount);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Could not load students.');
          this.loading.set(false);
        }
      });
  }

  setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  setClass(event: Event): void {
    this.classId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  setStatus(event: Event): void {
    this.status.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  go(page: number): void {
    this.page.set(page);
    this.load();
  }

  initials(name: string): string {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]?.toUpperCase() ?? '').join('');
  }
}
