import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../core/api.service';

interface Column { key: string; label: string; badge?: boolean; }
interface Config { title: string; subtitle: string; endpoint: string; columns: Column[]; }

@Component({
  selector: 'app-directory',
  standalone: true,
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Module</div>
          <h1>{{ config.title }}</h1>
          <p class="subtitle">{{ config.subtitle }}</p>
        </div>
        <div class="toolbar">
          <span class="badge teal">{{ rows().length }} records</span>
          <button class="btn btn-ghost btn-sm" (click)="load()" [disabled]="loading()">
            {{ loading() ? 'Refreshing…' : 'Refresh' }}
          </button>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }

      @if (loading()) {
        <div class="loading">Loading records…</div>
      } @else if (!rows().length) {
        <div class="empty">Nothing has been recorded in this module yet.</div>
      } @else {
        <section class="card">
          <div class="card-header">
            <input
              placeholder="Filter loaded records"
              [value]="filter()"
              (input)="setFilter($event)"
              style="max-width:320px;min-height:36px;border:1px solid var(--line);border-radius:9px;padding:6px 10px" />
            <span class="small muted">{{ visible().length }} shown</span>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  @for (column of config.columns; track column.key) {
                    <th>{{ column.label }}</th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (row of visible(); track trackRow(row)) {
                  <tr>
                    @for (column of config.columns; track column.key) {
                      <td>
                        @if (column.badge) {
                          <span [class]="'badge ' + badge(row[column.key])">{{ display(row[column.key]) }}</span>
                        } @else {
                          {{ display(row[column.key]) }}
                        }
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }
    </div>
  `
})
export class DirectoryComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  readonly config = this.route.snapshot.data as unknown as Config;
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly filter = signal('');
  readonly visible = signal<Record<string, unknown>[]>([]);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.get<Record<string, unknown>[]>(this.config.endpoint).subscribe({
      next: records => {
        const list = Array.isArray(records) ? records : [];
        this.rows.set(list);
        this.applyFilter();
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load records from this module.');
        this.loading.set(false);
      }
    });
  }

  setFilter(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
    this.applyFilter();
  }

  display(value: unknown): string {
    if (value === null || value === undefined || value === '') return '—';
    if (typeof value === 'boolean') return value ? 'Yes' : 'No';
    return String(value);
  }

  badge(value: unknown): string {
    if (value === true || value === 'Active' || value === 'Success' || value === 'Paid') return 'green';
    if (value === false || value === 'Inactive' || value === 'Failed') return 'red';
    if (value === 'Pending' || value === 'Draft') return 'amber';
    return 'teal';
  }

  trackRow(row: Record<string, unknown>): number {
    const id = row['id'];
    return typeof id === 'number' ? id : JSON.stringify(row).length;
  }

  private applyFilter(): void {
    const needle = this.filter().trim().toLowerCase();
    if (!needle) {
      this.visible.set(this.rows());
      return;
    }
    this.visible.set(
      this.rows().filter(row =>
        Object.values(row).some(value =>
          value !== null && value !== undefined && String(value).toLowerCase().includes(needle)
        )
      )
    );
  }
}
