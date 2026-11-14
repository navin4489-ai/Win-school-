import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { NotificationItem } from '../../core/models';
import { readError } from '../platform/schools.component';

interface Announcement {
  id: number; title: string; body: string; audience: string;
  publishedAtUtc?: string | null; expiresAtUtc?: string | null; createdAtUtc: string;
}

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [ReactiveFormsModule, DatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Communication</div>
          <h1>Notifications</h1>
          <p class="subtitle">In-app alerts and broadcast messages to staff, parents or students.</p>
        </div>
        <div class="toolbar">
          <label class="row small" style="gap:6px">
            <input type="checkbox" [checked]="unreadOnly()" (change)="toggleUnread($event)" />
            Unread only
          </label>
          @if (auth.has('communication.manage')) {
            <button class="btn btn-primary btn-sm" (click)="broadcastOpen.set(!broadcastOpen())">
              {{ broadcastOpen() ? 'Close' : 'New broadcast' }}
            </button>
          }
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      @if (broadcastOpen()) {
        <section class="card">
          <div class="card-header"><h3>Broadcast message</h3></div>
          <div class="card-body">
            <form class="form-grid" [formGroup]="form" (ngSubmit)="broadcast()">
              <label class="field full"><span>Title *</span><input formControlName="title" /></label>
              <label class="field full"><span>Message *</span><textarea formControlName="message"></textarea></label>
              <label class="field">
                <span>Audience</span>
                <select formControlName="audience">
                  <option value="All">Everyone</option>
                  <option value="Parents">Parents</option>
                  <option value="Students">Students</option>
                  <option value="Employees">Employees</option>
                </select>
              </label>
              <label class="field">
                <span>Channel</span>
                <select formControlName="channel">
                  <option value="InApp">In-app</option>
                  <option value="Email">Email</option>
                  <option value="Sms">SMS</option>
                  <option value="WhatsApp">WhatsApp</option>
                </select>
              </label>
              <div class="full">
                <button class="btn btn-primary" type="submit" [disabled]="saving() || form.invalid">
                  {{ saving() ? 'Sending…' : 'Send broadcast' }}
                </button>
              </div>
            </form>
          </div>
        </section>
      }

      @if (canManageAnnouncements()) {
        <section class="card">
          <div class="card-header">
            <h3>Announcements</h3>
            <button class="btn btn-ghost btn-sm" type="button" (click)="toggleAnnouncementForm()">
              {{ announcementFormOpen() ? 'Close' : 'New announcement' }}
            </button>
          </div>
          @if (announcementFormOpen()) {
            <div class="card-body">
              <form class="form-grid" [formGroup]="announcementForm" (ngSubmit)="saveAnnouncement()">
                <label class="field full"><span>Title *</span><input formControlName="title" maxlength="200" /></label>
                <label class="field full"><span>Message *</span><textarea formControlName="body" maxlength="4000"></textarea></label>
                <label class="field">
                  <span>Audience</span>
                  <select formControlName="audience">
                    <option value="All">Everyone</option>
                    <option value="Parents">Parents</option>
                    <option value="Students">Students</option>
                    <option value="Employees">Employees</option>
                  </select>
                </label>
                <label class="field"><span>Expires on</span><input type="date" formControlName="expiresOn" /></label>
                <div class="full row">
                  <button class="btn btn-primary" type="submit" [disabled]="saving() || announcementForm.invalid">
                    {{ saving() ? 'Saving…' : (editingAnnouncement() ? 'Save changes' : 'Publish announcement') }}
                  </button>
                  @if (editingAnnouncement()) {
                    <button class="btn btn-ghost" type="button" [disabled]="saving()" (click)="resetAnnouncement()">Cancel</button>
                  }
                </div>
              </form>
            </div>
          }
          @if (!announcements().length) {
            <div class="empty">No announcements yet.</div>
          } @else {
            <div class="table-wrap">
              <table>
                <thead><tr><th>Title</th><th>Audience</th><th>Published</th><th>Expires</th><th></th></tr></thead>
                <tbody>
                  @for (a of announcements(); track a.id) {
                    <tr>
                      <td>
                        <div class="primary">{{ a.title }}</div>
                        <div class="secondary">{{ a.body }}</div>
                      </td>
                      <td><span class="badge">{{ a.audience }}</span></td>
                      <td class="small muted">{{ (a.publishedAtUtc ?? a.createdAtUtc) | date:'dd MMM yyyy' }}</td>
                      <td class="small muted">{{ a.expiresAtUtc ? (a.expiresAtUtc | date:'dd MMM yyyy') : '-' }}</td>
                      <td>
                        <div class="row" style="gap:6px">
                          <button class="btn btn-ghost btn-sm" [disabled]="saving()" (click)="editAnnouncement(a)" [attr.aria-label]="'Edit announcement ' + a.title">Edit</button>
                          <button class="btn btn-ghost btn-sm" [disabled]="saving()" (click)="deleteAnnouncement(a)" [attr.aria-label]="'Delete announcement ' + a.title">Delete</button>
                        </div>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      }

      <section class="card">
        @if (loading()) {
          <div class="loading">Loading notifications…</div>
        } @else if (!items().length) {
          <div class="empty">No notifications yet.</div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead><tr><th>Title</th><th>Category</th><th>Channel</th><th>Status</th><th>Received</th><th></th></tr></thead>
              <tbody>
                @for (item of items(); track item.id) {
                  <tr>
                    <td>
                      <div class="primary">{{ item.title }}</div>
                      <div class="secondary">{{ item.message }}</div>
                    </td>
                    <td><span class="badge">{{ item.category }}</span></td>
                    <td>{{ item.channel }}</td>
                    <td><span [class]="'badge ' + (item.readAtUtc ? '' : 'teal')">{{ item.readAtUtc ? 'Read' : item.status }}</span></td>
                    <td class="small muted">{{ item.createdAtUtc | date:'dd MMM, HH:mm' }}</td>
                    <td>
                      @if (!item.readAtUtc) {
                        <button class="btn btn-ghost btn-sm" (click)="markRead(item)">Mark read</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </div>
  `
})
export class NotificationsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  readonly auth = inject(AuthService);

  readonly items = signal<NotificationItem[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly unreadOnly = signal(false);
  readonly broadcastOpen = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    title: ['', Validators.required],
    message: ['', Validators.required],
    audience: ['All'],
    channel: ['InApp']
  });

  ngOnInit(): void {
    this.load();
    this.loadAnnouncements();
  }

  readonly canManageAnnouncements = computed(() => this.auth.has('communication.manage'));
  readonly announcements = signal<Announcement[]>([]);
  readonly announcementFormOpen = signal(false);
  readonly editingAnnouncement = signal<Announcement | null>(null);

  readonly announcementForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    body: ['', [Validators.required, Validators.maxLength(4000)]],
    audience: ['All'],
    expiresOn: ['']
  });

  loadAnnouncements(): void {
    if (!this.canManageAnnouncements()) return;
    this.api.get<Announcement[]>('admin/announcements').subscribe({
      next: rows => this.announcements.set(rows),
      error: r => this.error.set(readError(r))
    });
  }

  toggleAnnouncementForm(): void {
    if (this.announcementFormOpen()) { this.resetAnnouncement(); return; }
    this.announcementFormOpen.set(true);
  }

  editAnnouncement(a: Announcement): void {
    this.editingAnnouncement.set(a);
    this.announcementForm.reset({
      title: a.title, body: a.body, audience: a.audience,
      expiresOn: a.expiresAtUtc ? toDateInput(a.expiresAtUtc) : ''
    });
    this.announcementFormOpen.set(true);
  }

  resetAnnouncement(): void {
    this.editingAnnouncement.set(null);
    this.announcementFormOpen.set(false);
    this.announcementForm.reset({ title: '', body: '', audience: 'All', expiresOn: '' });
  }

  saveAnnouncement(): void {
    if (this.announcementForm.invalid || this.saving()) return;
    const raw = this.announcementForm.getRawValue();
    const body = {
      title: raw.title.trim(), body: raw.body.trim(), audience: raw.audience,
      expiresAtUtc: raw.expiresOn ? new Date(`${raw.expiresOn}T23:59:59`).toISOString() : null
    };
    const editing = this.editingAnnouncement();
    this.saving.set(true);
    this.error.set(null);
    (editing ? this.api.put<Announcement>(`admin/announcements/${editing.id}`, body) : this.api.post<Announcement>('admin/announcements', body))
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.message.set(editing ? `Announcement "${body.title}" updated.` : `Announcement "${body.title}" published.`);
          this.resetAnnouncement();
          this.loadAnnouncements();
        },
        error: r => {
          this.saving.set(false);
          this.message.set(null);
          this.error.set(readError(r));
        }
      });
  }

  deleteAnnouncement(a: Announcement): void {
    if (this.saving() || !window.confirm(`Delete announcement "${a.title}"?`)) return;
    this.saving.set(true);
    this.error.set(null);
    this.api.delete(`admin/announcements/${a.id}`).subscribe({
      next: () => {
        this.saving.set(false);
        this.message.set(`Announcement "${a.title}" deleted.`);
        if (this.editingAnnouncement()?.id === a.id) this.resetAnnouncement();
        this.loadAnnouncements();
      },
      error: r => {
        this.saving.set(false);
        this.message.set(null);
        this.error.set(readError(r));
      }
    });
  }

  load(): void {
    this.loading.set(true);
    this.api.get<NotificationItem[]>('notifications', { unreadOnly: this.unreadOnly() }).subscribe({
      next: items => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load notifications.');
        this.loading.set(false);
      }
    });
  }

  toggleUnread(event: Event): void {
    this.unreadOnly.set((event.target as HTMLInputElement).checked);
    this.load();
  }

  markRead(item: NotificationItem): void {
    this.api.post(`notifications/${item.id}/read`).subscribe({
      next: () => {
        item.readAtUtc = new Date().toISOString();
        this.items.update(rows => [...rows]);
      },
      error: () => this.error.set('Could not mark the notification as read.')
    });
  }

  broadcast(): void {
    if (this.form.invalid || this.saving()) return;
    this.saving.set(true);
    this.api.post<{ recipients: number }>('notifications/broadcast', this.form.getRawValue()).subscribe({
      next: result => {
        this.saving.set(false);
        this.broadcastOpen.set(false);
        this.form.reset({ title: '', message: '', audience: 'All', channel: 'InApp' });
        this.message.set(`Broadcast queued for ${result.recipients} recipients.`);
        this.load();
      },
      error: () => {
        this.saving.set(false);
        this.error.set('Could not send the broadcast.');
      }
    });
  }
}

function asUtc(value: string): string {
  return /Z|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z';
}

function toDateInput(value: string): string {
  const d = new Date(asUtc(value));
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}