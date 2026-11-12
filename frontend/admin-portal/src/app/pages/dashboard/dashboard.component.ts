import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ChartPoint, Dashboard } from '../../core/models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DatePipe, CurrencyPipe, DecimalPipe, RouterLink],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <div class="eyebrow">Dashboard</div>
          <h1>{{ greeting() }}, {{ firstName() }}</h1>
          <p class="subtitle">
            {{ dashboard()?.academicSession || 'Current academic session' }} ·
            {{ auth.user()?.schoolName }}
          </p>
        </div>
        <div class="toolbar">
          <span class="badge teal">{{ dashboard()?.role || auth.user()?.role }}</span>
          <button class="btn btn-ghost btn-sm" (click)="load()" [disabled]="loading()">
            {{ loading() ? 'Refreshing…' : 'Refresh' }}
          </button>
        </div>
      </header>

      @if (error()) {
        <div class="error">{{ error() }}</div>
      }

      @if (loading()) {
        <div class="loading">Loading dashboard…</div>
      }
      @if (!loading() && dashboard(); as data) {
        <section class="stat-grid">
          @for (card of data.cards; track card.key) {
            <article [class]="'stat tone-' + card.tone">
              <div class="label">{{ card.label }}</div>
              <div class="value">
                {{ card.value }}@if (card.unit) {<span class="unit">{{ card.unit }}</span>}
              </div>
              @if (card.hint) { <div class="hint">{{ card.hint }}</div> }
            </article>
          }
        </section>

        <section class="split">
          <div style="display:grid; gap:20px">
            <article class="card">
              <div class="card-header">
                <div>
                  <h3>Fee collection</h3>
                  <span class="small muted">Last six months</span>
                </div>
                <span class="badge green">Collections</span>
              </div>
              <div class="card-body">
                @if (data.feeCollectionTrend.length) {
                  <div class="bar-chart">
                    @for (point of data.feeCollectionTrend; track point.label) {
                      <div class="bar-row">
                        <span class="muted">{{ point.label }}</span>
                        <div class="bar-track"><div class="bar-fill" [style.width.%]="percent(point, data.feeCollectionTrend)"></div></div>
                        <span class="amount">{{ point.value | currency:'INR':'symbol':'1.0-0' }}</span>
                      </div>
                    }
                  </div>
                } @else {
                  <div class="empty">No collection data yet.</div>
                }
              </div>
            </article>

            <article class="card">
              <div class="card-header">
                <div>
                  <h3>Attendance</h3>
                  <span class="small muted">Daily present percentage</span>
                </div>
                <a routerLink="/attendance" class="small">Open attendance →</a>
              </div>
              <div class="card-body">
                @if (data.attendanceTrend.length) {
                  <div class="bar-chart">
                    @for (point of data.attendanceTrend; track point.label) {
                      <div class="bar-row">
                        <span class="muted">{{ point.label }}</span>
                        <div class="bar-track"><div class="bar-fill" [style.width.%]="point.value"></div></div>
                        <span class="amount">{{ point.value | number:'1.0-0' }}%</span>
                      </div>
                    }
                  </div>
                } @else {
                  <div class="empty">Attendance has not been marked yet.</div>
                }
              </div>
            </article>
          </div>

          <div style="display:grid; gap:20px">
            <article class="card">
              <div class="card-header"><h3>Today</h3><span class="small muted">Timetable</span></div>
              <div class="card-body">
                @if (data.todayTimetable.length) {
                  <div class="list">
                    @for (slot of data.todayTimetable; track slot.period + slot.subject) {
                      <div class="list-item">
                        <div class="dot"></div>
                        <div style="flex:1">
                          <div style="font-weight:600">{{ slot.subject }}</div>
                          <div class="small muted">{{ slot.class }} · {{ slot.period }} · {{ slot.startTime }}–{{ slot.endTime }}</div>
                        </div>
                      </div>
                    }
                  </div>
                } @else {
                  <div class="empty">No classes scheduled for you today.</div>
                }
              </div>
            </article>

            <article class="card">
              <div class="card-header"><h3>Recent activity</h3><a routerLink="/notifications" class="small">All →</a></div>
              <div class="card-body">
                @if (data.recentActivity.length) {
                  <div class="list">
                    @for (item of data.recentActivity; track item.atUtc + item.title) {
                      <div class="list-item">
                        <div [class]="'dot ' + toneDot(item.tone)"></div>
                        <div style="flex:1">
                          <div style="font-weight:600">{{ item.title }}</div>
                          <div class="small muted">{{ item.detail }}</div>
                        </div>
                        <span class="small muted">{{ item.atUtc | date:'shortTime' }}</span>
                      </div>
                    }
                  </div>
                } @else {
                  <div class="empty">No activity recorded yet.</div>
                }
              </div>
            </article>
          </div>
        </section>
      }
    </div>
  `
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);

  readonly dashboard = signal<Dashboard | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly firstName = computed(() => (this.auth.user()?.fullName ?? 'there').split(' ')[0]);
  readonly greeting = computed(() => {
    const hour = new Date().getHours();
    if (hour < 12) return 'Good morning';
    if (hour < 17) return 'Good afternoon';
    return 'Good evening';
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.get<Dashboard>('dashboard').subscribe({
      next: data => {
        this.dashboard.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load the dashboard. Please try again.');
        this.loading.set(false);
      }
    });
  }

  percent(point: ChartPoint, points: ChartPoint[]): number {
    const max = Math.max(...points.map(p => p.value), 1);
    return Math.round((point.value / max) * 100);
  }

  toneDot(tone: string): string {
    if (tone === 'positive') return 'green';
    if (tone === 'warning') return 'amber';
    if (tone === 'danger') return 'red';
    return '';
  }
}
