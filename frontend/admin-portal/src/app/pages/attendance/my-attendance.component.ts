import { localDate } from '../../core/dates';
import { DatePipe } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

interface StaffAttendance {
  employeeId: number;
  employeeName: string;
  employeeNo: string;
  date: string;
  checkInUtc?: string | null;
  checkOutUtc?: string | null;
  status: string;
  isLate: boolean;
  checkInPhotoUrl?: string | null;
  checkOutPhotoUrl?: string | null;
  checkInLatitude?: number | null;
  checkInLongitude?: number | null;
  checkOutLatitude?: number | null;
  checkOutLongitude?: number | null;
  checkInAddress?: string | null;
  checkOutAddress?: string | null;
}

interface StaffAttendanceRange {
  employeeId: number;
  employeeName: string;
  employeeNo: string;
  from: string;
  to: string;
  totalDays: number;
  present: number;
  absent: number;
  late: number;
  hoursWorked: number;
  records: StaffAttendance[];
}

interface LocationFix {
  latitude: number;
  longitude: number;
  address: string;
}

const DAY_MS = 86400000;

/**
 * Staff attendance: check in and check out with a photo from the device camera and
 * the current location attached to the record.
 *
 * The camera uses the user-facing stream when the device offers one, so a teacher
 * photographs themselves rather than the room. The capture is uploaded through the
 * file endpoint and the returned URL is stored with the attendance row.
 */
@Component({
  selector: 'app-my-attendance',
  standalone: true,
  imports: [FormsModule, DatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Staff</div>
          <h1>My attendance</h1>
          <p class="subtitle">Check in with a photo and your location so the register is verifiable.</p>
        </div>
        <div class="toolbar">
          <span class="badge" [class.green]="connected()" [class.amber]="!connected()">
            {{ connected() ? 'Live' : 'Offline' }}
          </span>
        </div>
      </header>

      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (message()) { <div class="success">{{ message() }}</div> }

      <div class="split">
        <section class="card">
          <div class="card-header">
            <h3>Check in / check out</h3>
            <span class="small muted">{{ today }}</span>
          </div>
          <div class="card-body stack">
            <div class="capture" [class.has-photo]="!!photoPreview()">
              @if (photoPreview()) {
                <img [src]="photoPreview()" alt="Captured attendance photo" />
              } @else {
                <div class="capture-empty">
                  <div class="capture-icon">📷</div>
                  <p>No photo captured yet.</p>
                  <p class="small muted">A front-camera photo is attached to the register entry.</p>
                </div>
              }
            </div>

            <div class="row">
              <button class="btn btn-ghost btn-sm" (click)="startCamera()" [disabled]="cameraOn()">
                {{ cameraOn() ? 'Camera running' : 'Open front camera' }}
              </button>
              <button class="btn btn-ghost btn-sm" (click)="capturePhoto()" [disabled]="!cameraOn()">Capture photo</button>
              <button class="btn btn-ghost btn-sm" (click)="stopCamera()" [disabled]="!cameraOn()">Stop camera</button>
              <button class="btn btn-ghost btn-sm" (click)="photoPreview.set(null); photoUrl.set(null)">
                Clear photo
              </button>
            </div>

            <div class="location">
              <div class="row">
                <button class="btn btn-ghost btn-sm" (click)="captureLocation()" [disabled]="locating()">
                  {{ locating() ? 'Locating…' : 'Capture location' }}
                </button>
                @if (location()) {
                  <span class="badge teal">{{ location()!.latitude }}, {{ location()!.longitude }}</span>
                  @if (location()!.address) {
                    <span class="small muted">{{ location()!.address }}</span>
                  }
                } @else {
                  <span class="small muted">Location not captured</span>
                }
              </div>
            </div>

            <div class="divider"></div>

            <div class="row">
              <button class="btn btn-primary" (click)="checkIn()" [disabled]="busy()">
                {{ busy() ? 'Working…' : 'Check in' }}
              </button>
              <button class="btn btn-gold" (click)="checkOut()" [disabled]="busy()">Check out</button>
              @if (todayRecord(); as record) {
                <span class="small muted">
                  In {{ record.checkInUtc | date:'HH:mm' }} ·
                  Out {{ record.checkOutUtc ? (record.checkOutUtc | date:'HH:mm') : '—' }}
                </span>
              }
            </div>
          </div>
        </section>

        <section class="card">
          <div class="card-header"><h3>My register</h3></div>
          <div class="card-body stack">
            <div class="row">
              <label class="field" style="flex:1">
                <span>From</span>
                <input type="date" [(ngModel)]="from" (ngModelChange)="load()" />
              </label>
              <label class="field" style="flex:1">
                <span>To</span>
                <input type="date" [(ngModel)]="to" (ngModelChange)="load()" />
              </label>
            </div>

            @if (summary(); as data) {
              <div class="stat-grid">
                <div class="stat tone-positive">
                  <div class="label">Present</div>
                  <div class="value">{{ data.present }}</div>
                </div>
                <div class="stat tone-warning">
                  <div class="label">Late</div>
                  <div class="value">{{ data.late }}</div>
                </div>
                <div class="stat tone-danger">
                  <div class="label">Absent</div>
                  <div class="value">{{ data.absent }}</div>
                </div>
                <div class="stat tone-info">
                  <div class="label">Hours</div>
                  <div class="value">{{ data.hoursWorked }}<span class="unit">h</span></div>
                </div>
              </div>
            }

            @if (loading()) {
              <div class="loading">Loading register…</div>
            } @else if (!records().length) {
              <div class="empty">No attendance recorded in this period.</div>
            } @else {
              <div class="table-wrap">
                <table>
                  <thead>
                    <tr><th>Date</th><th>In</th><th>Out</th><th>Status</th><th>Photo</th></tr>
                  </thead>
                  <tbody>
                    @for (record of records(); track record.date) {
                      <tr>
                        <td>{{ record.date | date:'dd MMM yyyy' }}</td>
                        <td class="mono">{{ record.checkInUtc ? (record.checkInUtc | date:'HH:mm') : '—' }}</td>
                        <td class="mono">{{ record.checkOutUtc ? (record.checkOutUtc | date:'HH:mm') : '—' }}</td>
                        <td>
                          <span class="badge"
                            [class.green]="record.status === 'Present'"
                            [class.amber]="record.status === 'Late'"
                            [class.red]="record.status === 'Absent'">
                            {{ record.status }}
                          </span>
                        </td>
                        <td>
                          <div class="thumbs">
                            @if (record.checkInPhotoUrl) {
                              <a [href]="record.checkInPhotoUrl" target="_blank" title="Check-in photo">
                                <img [src]="record.checkInPhotoUrl" alt="Check-in photo" />
                              </a>
                            }
                            @if (record.checkOutPhotoUrl) {
                              <a [href]="record.checkOutPhotoUrl" target="_blank" title="Check-out photo">
                                <img [src]="record.checkOutPhotoUrl" alt="Check-out photo" />
                              </a>
                            }
                            @if (!record.checkInPhotoUrl && !record.checkOutPhotoUrl) {
                              <span class="small muted">—</span>
                            }
                          </div>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        </section>
      </div>
    </div>

    @if (cameraOn()) {
      <div class="camera-overlay" (click)="stopCamera()">
        <div class="camera-frame" (click)="$event.stopPropagation()">
          <video #video playsinline muted autoplay></video>
          <div class="camera-hint">Front camera preview — use "Capture photo" in the panel</div>
        </div>
      </div>
    }
  `,
  styles: [`
    .stack { display: grid; gap: 14px; }
    .capture { position: relative; aspect-ratio: 4 / 3; border-radius: var(--radius-sm); overflow: hidden; background: #0d1b2a; display: grid; place-items: center; }
    .capture img { width: 100%; height: 100%; object-fit: cover; }
    .capture-empty { text-align: center; color: #9fb7cb; padding: 20px; }
    .capture-icon { font-size: 34px; }
    .location { padding: 12px; border: 1px dashed var(--ink-300); border-radius: var(--radius-sm); }
    .thumbs { display: flex; gap: 6px; }
    .thumbs img { width: 34px; height: 34px; object-fit: cover; border-radius: 7px; border: 1px solid var(--line); }
    .camera-overlay { position: fixed; inset: 0; background: rgba(6, 20, 35, 0.72); display: grid; place-items: center; z-index: 80; padding: 20px; }
    .camera-frame { width: min(420px, 100%); background: #000; border-radius: var(--radius); overflow: hidden; }
    .camera-frame video { width: 100%; display: block; transform: scaleX(-1); }
    .camera-hint { padding: 9px 12px; color: #9fb7cb; font-size: 12px; text-align: center; }
  `]
})
export class MyAttendanceComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  @ViewChild('video') videoRef?: ElementRef<HTMLVideoElement>;

  readonly today = new Date().toLocaleDateString(undefined, { dateStyle: 'full' });
  readonly from = this.daysAgo(30);
  readonly to = this.daysAgo(0);

  readonly records = signal<StaffAttendance[]>([]);
  readonly summary = signal<StaffAttendanceRange | null>(null);
  readonly todayRecord = signal<StaffAttendance | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly locating = signal(false);
  readonly cameraOn = signal(false);
  readonly connected = signal(typeof navigator === 'undefined' ? true : navigator.onLine);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly photoPreview = signal<string | null>(null);
  readonly photoUrl = signal<string | null>(null);
  readonly location = signal<LocationFix | null>(null);

  private stream: MediaStream | null = null;

  ngOnInit(): void {
    this.load();
    window.addEventListener('online', this.updateConnection);
    window.addEventListener('offline', this.updateConnection);
  }

  ngOnDestroy(): void {
    this.stopCamera();
    window.removeEventListener('online', this.updateConnection);
    window.removeEventListener('offline', this.updateConnection);
  }

  private readonly updateConnection = () => this.connected.set(navigator.onLine);

  load(): void {
    this.loading.set(true);
    this.api.get<StaffAttendanceRange>('attendance/staff/me', { from: this.from, to: this.to }).subscribe({
      next: data => {
        this.records.set(data.records);
        this.summary.set(data);
        this.loading.set(false);
        const todayRow = data.records.find(r => r.date === this.isoToday());
        this.todayRecord.set(todayRow ?? null);
      },
      error: () => {
        this.error.set('Could not load your register.');
        this.loading.set(false);
      }
    });
  }

  async startCamera(): Promise<void> {
    this.error.set(null);

    if (!navigator.mediaDevices?.getUserMedia) {
      this.error.set('This browser does not expose a camera. Upload a photo instead or use the mobile app.');
      return;
    }

    try {
      // facingMode 'user' selects the front camera when the device has one.
      this.stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 960 } },
        audio: false
      });

      this.cameraOn.set(true);
      // The <video> element only exists once the overlay renders.
      setTimeout(() => {
        const video = this.videoRef?.nativeElement;
        if (video) {
          video.srcObject = this.stream;
          void video.play();
        }
      });
    } catch {
      this.error.set('The camera could not be started. Check the browser permission and try again.');
    }
  }

  stopCamera(): void {
    this.stream?.getTracks().forEach(track => track.stop());
    this.stream = null;
    this.cameraOn.set(false);
  }

  capturePhoto(): void {
    const video = this.videoRef?.nativeElement;
    if (!video || !video.videoWidth) {
      this.error.set('The camera is not ready yet.');
      return;
    }

    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;

    const context = canvas.getContext('2d');
    if (!context) return;

    // Mirror the image so it matches what the teacher sees in the preview.
    context.translate(canvas.width, 0);
    context.scale(-1, 1);
    context.drawImage(video, 0, 0, canvas.width, canvas.height);

    canvas.toBlob(blob => {
      if (!blob) return;
      void this.upload(blob);
    }, 'image/jpeg', 0.82);
  }

  captureLocation(): void {
    this.locating.set(true);
    this.error.set(null);

    if (!navigator.geolocation) {
      this.locating.set(false);
      this.error.set('This browser does not expose the location API.');
      return;
    }

    navigator.geolocation.getCurrentPosition(
      position => {
        this.locating.set(false);
        this.location.set({
          latitude: Number(position.coords.latitude.toFixed(6)),
          longitude: Number(position.coords.longitude.toFixed(6)),
          address: ''
        });
        void this.resolveAddress(position.coords.latitude, position.coords.longitude);
      },
      () => {
        this.locating.set(false);
        this.error.set('The location could not be determined. Allow location access and try again.');
      },
      { enableHighAccuracy: true, timeout: 15000, maximumAge: 60000 }
    );
  }

  checkIn(): void {
    void this.submit('attendance/staff/check-in');
  }

  checkOut(): void {
    void this.submit('attendance/staff/check-out');
  }

  private async submit(path: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);

    const fix = this.location();
    const body = {
      photoUrl: this.photoUrl(),
      latitude: fix?.latitude ?? null,
      longitude: fix?.longitude ?? null,
      address: fix?.address ?? null,
      source: 'Mobile'
    };

    try {
      await fetch(`/api/${path}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${localStorage.getItem('school_erp_access') ?? ''}`
        },
        body: JSON.stringify(body)
      }).then(async response => {
        if (!response.ok) {
          const problem = await response.json().catch(() => ({}));
          throw new Error((problem as { detail?: string }).detail ?? `Request failed (${response.status}).`);
        }
        return response;
      });

      this.message.set(path.includes('check-in') ? 'Checked in successfully.' : 'Checked out successfully.');
      this.photoPreview.set(null);
      this.photoUrl.set(null);
      this.load();
    } catch (error) {
      this.error.set((error as Error).message);
    } finally {
      this.busy.set(false);
    }
  }

  private async upload(blob: Blob): Promise<void> {
    const form = new FormData();
    form.append('file', blob, `attendance-${Date.now()}.jpg`);

    try {
      const response = await fetch('/api/files/upload', {
        method: 'POST',
        headers: { Authorization: `Bearer ${localStorage.getItem('school_erp_access') ?? ''}` },
        body: form
      });

      if (!response.ok) throw new Error(`Upload failed (${response.status}).`);

      const uploaded = (await response.json()) as { url: string };
      this.photoUrl.set(uploaded.url);
      this.photoPreview.set(uploaded.url);
      this.message.set('Photo captured.');
    } catch (error) {
      this.error.set((error as Error).message);
    }
  }

  /** Reverse geocodes the fix so the register shows a readable location. */
  private async resolveAddress(latitude: number, longitude: number): Promise<void> {
    try {
      const response = await fetch(
        `https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat=${latitude}&lon=${longitude}&zoom=16`,
        { headers: { Accept: 'application/json' } }
      );

      if (!response.ok) return;
      const place = (await response.json()) as { display_name?: string };
      const address = place.display_name ?? '';
      if (!address) return;

      this.location.update(current => (current ? { ...current, address } : current));
    } catch {
      // A missing address label is cosmetic; the coordinates are still recorded.
    }
  }

  private daysAgo(days: number): string {
    return localDate(new Date(Date.now() - days * DAY_MS));
  }

  private isoToday(): string {
    return localDate();
  }
}
