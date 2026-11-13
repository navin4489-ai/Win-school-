import { Injectable, NgZone, inject, signal } from '@angular/core';
import { NotificationItem } from './models';

/**
 * Live notification centre.
 *
 * Subscribes to the server-sent-events stream so a message sent by the principal
 * reaches the browser the moment it is written. Every incoming message raises a
 * popup and plays a bell tone.
 *
 * The stream is read with fetch() rather than EventSource because EventSource cannot
 * carry the bearer token, and putting a token in the query string would leak it into
 * proxy and access logs.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly zone = inject(NgZone);
  private controller: AbortController | null = null;
  private audioContext: AudioContext | null = null;
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private stopped = true;

  readonly unreadCount = signal(0);
  readonly toasts = signal<NotificationItem[]>([]);
  readonly latest = signal<NotificationItem | null>(null);
  readonly connected = signal(false);
  readonly soundEnabled = signal(this.readSoundPreference());

  private readonly TOAST_TTL = 8000;

  connect(): void {
    this.stopped = false;
    void this.runStream();
    // The stream is the fast path; polling is the safety net for proxies that
    // buffer or drop long lived connections.
    this.startPolling();
  }

  disconnect(): void {
    this.stopped = true;
    this.controller?.abort();
    this.controller = null;
    this.connected.set(false);

    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private async runStream(): Promise<void> {
    let backoff = 2000;

    while (!this.stopped) {
      try {
        await this.openStream();
        backoff = 2000;
      } catch {
        // Fall through to the reconnect below.
      }

      if (this.stopped) return;
      this.connected.set(false);

      // Reconnect with a bounded backoff so a server restart or a dropped
      // connection recovers without hammering the API.
      await new Promise(resolve => setTimeout(resolve, backoff));
      backoff = Math.min(backoff * 2, 30000);
    }
  }

  private async openStream(): Promise<void> {
    const token = localStorage.getItem('school_erp_access');
    if (!token) return;

    this.controller?.abort();
    const controller = new AbortController();
    this.controller = controller;

    const response = await fetch('/api/notifications/stream', {
      headers: { Authorization: `Bearer ${token}`, Accept: 'text/event-stream' },
      signal: controller.signal
    });

    if (!response.ok || !response.body) throw new Error(`stream responded ${response.status}`);

    this.connected.set(true);
    await this.readFrames(response.body);
  }

  dismiss(id: number): void {
    this.toasts.update(list => list.filter(toast => toast.id !== id));
  }

  toggleSound(): void {
    this.soundEnabled.update(enabled => !enabled);
    localStorage.setItem('school_erp_sound', this.soundEnabled() ? '1' : '0');
    if (this.soundEnabled()) this.playBell();
  }

  markRead(item: NotificationItem): void {
    item.readAtUtc = new Date().toISOString();
    this.unreadCount.update(count => Math.max(0, count - 1));
  }

  refreshCount(): void {
    void this.fetchUnreadCount();
  }

  private readSoundPreference(): boolean {
    return localStorage.getItem('school_erp_sound') !== '0';
  }

  private async startStream(): Promise<void> {
    const token = localStorage.getItem('school_erp_access');
    if (!token) return;

    this.controller?.abort();
    const controller = new AbortController();
    this.controller = controller;

    try {
      const response = await fetch('/api/notifications/stream', {
        headers: { Authorization: `Bearer ${token}`, Accept: 'text/event-stream' },
        signal: controller.signal
      });

      if (!response.ok || !response.body) throw new Error(`stream responded ${response.status}`);

      this.connected.set(true);
      await this.readFrames(response.body);
    } catch (error) {
      if (this.stopped || (error as Error)?.name === 'AbortError') return;
      this.connected.set(false);
    }
  }

  private async readFrames(body: ReadableStream<Uint8Array>): Promise<void> {
    const reader = body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    try {
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });

        let boundary = buffer.indexOf('\n\n');
        while (boundary !== -1) {
          const frame = buffer.slice(0, boundary);
          buffer = buffer.slice(boundary + 2);
          this.handleFrame(frame);
          boundary = buffer.indexOf('\n\n');
        }
      }
    } finally {
      this.connected.set(false);
    }
  }

  private handleFrame(frame: string): void {
    const data = frame
      .split('\n')
      .filter(line => line.startsWith('data:'))
      .map(line => line.slice(5).trim())
      .join('');

    if (!data) return;

    try {
      const notification = JSON.parse(data) as NotificationItem;
      this.zone.run(() => this.receive(notification));
    } catch {
      // Ignore malformed frames.
    }
  }

  private receive(notification: NotificationItem): void {
    this.latest.set(notification);
    if (!notification.readAtUtc) this.unreadCount.update(count => count + 1);

    this.toasts.update(list => {
      if (list.some(toast => toast.id === notification.id)) return list;
      return [...list, notification].slice(-4);
    });

    this.playBell();

    setTimeout(() => this.dismiss(notification.id), this.TOAST_TTL);
  }

  private startPolling(): void {
    if (this.pollTimer) clearInterval(this.pollTimer);

    this.pollTimer = setInterval(() => {
      if (this.stopped) return;
      void this.fetchUnreadCount();
    }, 30000);
  }

  private async fetchUnreadCount(): Promise<void> {
    try {
      const response = await fetch('/api/notifications/unread-count', {
        headers: { Authorization: `Bearer ${localStorage.getItem('school_erp_access') ?? ''}` }
      });

      if (!response.ok) return;
      const count = (await response.json()) as number;
      this.zone.run(() => this.unreadCount.set(count));
    } catch {
      // The badge keeps its last known value.
    }
  }

  /**
   * Bell tone synthesised with the Web Audio API, so no audio asset has to be
   * shipped or cached and the sound works offline.
   */
  private playBell(): void {
    if (!this.soundEnabled()) return;

    try {
      const Ctor = window.AudioContext ?? (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
      if (!Ctor) return;

      this.audioContext ??= new Ctor();
      const context = this.audioContext;
      if (context.state === 'suspended') void context.resume();

      const now = context.currentTime;
      const master = context.createGain();
      master.gain.setValueAtTime(0.0001, now);
      master.gain.exponentialRampToValueAtTime(0.22, now + 0.02);
      master.gain.exponentialRampToValueAtTime(0.0001, now + 1.1);
      master.connect(context.destination);

      // Two struck partials give a recognisable "bell" rather than a plain beep.
      [880, 1320].forEach((frequency, index) => {
        const oscillator = context.createOscillator();
        const gain = context.createGain();
        oscillator.type = 'sine';
        oscillator.frequency.setValueAtTime(frequency, now);
        gain.gain.setValueAtTime(index === 0 ? 1 : 0.5, now);
        oscillator.connect(gain).connect(master);
        oscillator.start(now + index * 0.09);
        oscillator.stop(now + 1.1);
      });
    } catch {
      // Audio is a nicety; never let it break the notification flow.
    }
  }
}
