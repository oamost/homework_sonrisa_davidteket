import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface Subscription {
  id: number;
  eventType: string;
  channel: string;
  destination: string | null;
  enabled: boolean;
}

interface EmailStatus {
  configured: boolean; enabled: boolean; host: string | null; port: number | null;
  tlsMode: string | null; fromAddress: string | null; hasCredentials: boolean; csrfToken: string;
}

const eventTypes = [
  {code: 'world-news', name: 'World news', description: 'Important breaking world stories.'},
  {code: 'conflict', name: 'Armed conflict', description: 'Significant conflict reports.'},
  {code: 'politics', name: 'Politics', description: 'Consequential political news.'},
  {code: 'diplomacy', name: 'Diplomacy', description: 'Major diplomatic developments.'},
  {code: 'disaster.earthquake', name: 'Earthquake', description: 'Major earthquakes.'},
  {code: 'disaster.flood', name: 'Flood', description: 'Major floods.'},
  {code: 'disaster.cyclone', name: 'Cyclone', description: 'Major tropical cyclones.'},
  {code: 'disaster.wildfire', name: 'Wildfire', description: 'Major wildfires.'},
  {code: 'disaster.volcano', name: 'Volcano', description: 'Major volcanic activity.'},
  {code: 'disaster.drought', name: 'Drought', description: 'Major droughts.'},
  {code: 'disaster.tsunami', name: 'Tsunami', description: 'Tsunami warnings and events; standalone live feed unproven.'},
  {code: 'disaster.landslide', name: 'Landslide', description: 'Major landslides; standalone live feed unproven.'},
  {code: 'disaster.epidemic', name: 'Epidemic', description: 'Major outbreak and epidemic reports.'},
  {code: 'market.crypto', name: 'Crypto markets', description: 'Important crypto market moves.'},
  {code: 'market.fx', name: 'FX markets', description: 'Important currency market moves.'},
  {code: 'market.indices', name: 'Equity indices', description: 'Important index moves; connected sources may be delayed or regional.'},
  {code: 'health.humanitarian', name: 'Humanitarian emergency', description: 'Severe humanitarian emergencies.'},
  {code: 'tech.cyber', name: 'Cyber incidents', description: 'Consequential cyber news.'},
  {code: 'tech.ai', name: 'AI developments', description: 'Consequential AI news.'},
  {code: 'tech.acquisitions', name: 'Major acquisitions', description: 'Consequential technology acquisitions.'},
  {code: 'tech.critical-infrastructure', name: 'Critical infrastructure', description: 'Consequential infrastructure news.'},
  {code: 'environment.energy', name: 'Environment and energy', description: 'Consequential environment and energy news.'},
  {code: 'science.transformative', name: 'Transformative science', description: 'Important science news; topic filtering is not proven.'},
] as const;

@Component({
  selector: 'app-subscriptions',
  standalone: true,
  templateUrl: './subscriptions.component.html',
})
export class SubscriptionsComponent {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly eventTypes = eventTypes;
  protected readonly items = signal<Subscription[] | null>(null);
  protected readonly loading = signal(false);
  protected readonly pending = signal(false);
  protected readonly loadError = signal(false);
  protected readonly actionError = signal('');
  protected readonly notice = signal('');
  protected readonly validation = signal('');
  protected readonly confirmId = signal<number | null>(null);
  protected editing = false;
  protected eventType = '';
  protected channel = 'email';
  protected destination = '';
  protected enabled = true;
  protected readonly emailStatus = signal<EmailStatus | null>(null);
  protected readonly emailAccess = signal<'loading' | 'admin' | 'login' | 'error'>('loading');
  protected readonly emailBusy = signal(false);
  protected readonly emailMessage = signal('');
  protected readonly emailError = signal('');
  protected smtpHost = '';
  protected smtpPort = 587;
  protected smtpUsername = '';
  protected smtpPassword = '';
  protected smtpFrom = '';
  protected smtpEnabled = false;
  protected testDestination = '';

  constructor() { this.refresh(); this.refreshEmail(); }

  protected refreshEmail(): void {
    this.http.get<EmailStatus>('/api/admin/email').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: status => {
        this.emailStatus.set(status); this.emailAccess.set('admin');
        this.smtpHost = status.host ?? ''; this.smtpPort = status.port ?? 587;
        this.smtpFrom = status.fromAddress ?? ''; this.smtpEnabled = status.enabled;
      },
      error: error => this.emailAccess.set(error.status === 401 || error.status === 403 ? 'login' : 'error'),
    });
  }

  protected saveEmail(): void {
    const status = this.emailStatus();
    if (!status || this.emailBusy()) return;
    this.emailBusy.set(true); this.emailMessage.set(''); this.emailError.set('');
    this.http.put<EmailStatus>('/api/admin/email', {
      host: this.smtpHost, port: Number(this.smtpPort), tlsMode: 'StartTls',
      username: this.smtpUsername, password: this.smtpPassword,
      fromAddress: this.smtpFrom, enabled: this.smtpEnabled,
    }, {headers: {'X-CSRF-TOKEN': status.csrfToken}}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: saved => {
        this.emailStatus.set({...saved, csrfToken: status.csrfToken});
        this.smtpPassword = ''; this.emailBusy.set(false);
        this.emailMessage.set(saved.enabled ? 'SMTP saved and enabled. Eligible email alerts can now be sent.' : 'SMTP saved with automatic delivery disabled.');
      },
      error: error => {
        this.smtpPassword = ''; this.emailBusy.set(false);
        this.emailError.set(error.status === 400 && typeof error.error?.error === 'string'
          ? error.error.error : 'Unable to save SMTP settings. Check your admin login and try again.');
      },
    });
  }

  protected sendTestEmail(): void {
    const status = this.emailStatus();
    if (!status?.enabled || this.emailBusy()) return;
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.testDestination.trim())) {
      this.emailError.set('Enter a valid test destination.'); return;
    }
    this.emailBusy.set(true); this.emailError.set(''); this.emailMessage.set('');
    this.http.post<{message: string}>('/api/admin/email/test', {destination: this.testDestination.trim()},
      {headers: {'X-CSRF-TOKEN': status.csrfToken}}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => { this.testDestination = ''; this.emailBusy.set(false); this.emailMessage.set(result.message); },
      error: error => {
        this.testDestination = ''; this.emailBusy.set(false);
        this.emailError.set(error.status === 502 ? 'SMTP did not accept the test email. Check host, STARTTLS, credentials, sender and recipient.'
          : 'Test email could not be sent. Check your admin login and SMTP settings.');
      },
    });
  }

  refresh(): void {
    if (this.loading() || this.pending()) return;
    this.loading.set(true);
    this.loadError.set(false);
    this.http.get<Subscription[]>('/api/subscriptions').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: items => { this.items.set(items); this.loading.set(false); },
      error: () => { this.loadError.set(true); this.loading.set(false); },
    });
  }

  protected nameFor(code: string): string {
    return this.eventTypes.find(type => type.code === code)?.name ?? 'Unknown event type';
  }

  protected edit(item: Subscription): void {
    if (this.pending()) return;
    this.eventType = item.eventType;
    this.channel = item.channel;
    this.destination = '';
    this.enabled = item.enabled;
    this.editing = true;
    this.validation.set('');
    this.notice.set('');
    this.actionError.set('');
    this.confirmId.set(null);
  }

  protected save(): void {
    if (this.pending() || this.loading()) return;
    this.validation.set('');
    this.actionError.set('');
    this.notice.set('');
    if (!this.eventTypes.some(type => type.code === this.eventType)) {
      this.validation.set('Choose an approved event type.'); return;
    }
    if (this.channel !== 'email' && this.channel !== 'slack') {
      this.validation.set('Choose email or Slack.'); return;
    }
    const previous = this.items()?.find(item => item.eventType === this.eventType && item.channel === this.channel);
    const destination = this.destination.trim() || previous?.destination?.trim() || '';
    if (this.channel === 'email' && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(destination)) {
      this.validation.set('Enter a valid email destination.'); return;
    }
    // SubscriptionStore explicitly stores destination as plaintext, not a credential.
    // Only a non-secret Slack channel reference is safe here; the sender webhook belongs in private configuration.
    if (this.channel === 'slack' && !/^channel:#[a-zA-Z0-9_-]+$/.test(destination)) {
      this.validation.set('Enter a Slack channel reference like channel:#alerts, not a webhook secret.'); return;
    }
    this.pending.set(true);
    this.http.post<Subscription>('/api/subscriptions', {
      eventType: this.eventType, channel: this.channel, destination, enabled: this.enabled,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: saved => {
        this.items.update(items => {
          const rest = (items ?? []).filter(item => item.id !== saved.id && !(item.eventType === saved.eventType && item.channel === saved.channel));
          return [...rest, saved];
        });
        this.destination = '';
        this.editing = false;
        this.notice.set('Saved subscription. Email requires enabled SMTP and matching eligible events; delivery is not guaranteed.');
        this.pending.set(false);
      },
      error: () => { this.actionError.set('Unable to save subscription. Try again.'); this.pending.set(false); },
    });
  }

  protected delete(item: Subscription): void {
    if (this.pending()) return;
    this.confirmId.set(item.id);
    this.actionError.set('');
  }

  protected cancelDelete(): void { this.confirmId.set(null); }

  protected confirmDelete(id: number): void {
    if (this.pending()) return;
    this.pending.set(true);
    this.actionError.set('');
    this.http.delete(`/api/subscriptions/${id}`).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.items.update(items => (items ?? []).filter(item => item.id !== id));
        this.confirmId.set(null);
        this.notice.set('Subscription deleted.');
        this.pending.set(false);
      },
      error: () => { this.actionError.set('Unable to delete subscription. Try again.'); this.pending.set(false); },
    });
  }
}
