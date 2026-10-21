import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface SlackStatus {
  configured: boolean;
  enabled: boolean;
  configId: string;
  target: string;
  channelLabel?: string | null;
  csrfToken: string;
}

@Component({
  selector: 'app-slack-setup',
  standalone: true,
  template: `
    <section class="card mb-4" aria-labelledby="slack-heading"><div class="card-body">
      <h3 id="slack-heading" class="h5">Private Slack delivery setup</h3>
      @if (access() === 'loading') { <p>Checking Slack admin access…</p> }
      @if (access() === 'login') { <p>Sign in as the local administrator at <a href="/admin/login">Admin login</a>, then reload to configure Slack.</p> }
      @if (access() === 'error') { <p class="alert alert-danger">Slack settings unavailable. Reload to retry.</p> }
      @if (access() === 'admin') {
         <p class="small text-secondary">{{ status()?.enabled ? 'Ready for automatic alerts' : 'Needs setup: add an authorized Slack incoming webhook' }}. Signing in to Slack does not grant the app webhook access. In Slack, create an incoming webhook for the channel you choose and paste its URL here. The saved URL is never displayed. @if (status()?.channelLabel) { Channel: {{ status()?.channelLabel }}. }</p>
         <form (submit)="$event.preventDefault(); save()" novalidate>
           <label class="form-label" for="slack-label">Channel label (non-secret)</label>
           <input id="slack-label" name="slackLabel" type="text" class="form-control mb-2" autocomplete="off" [value]="label" (input)="label = $any($event.target).value" [disabled]="busy()" placeholder="#alerts">
          <label class="form-label" for="slack-webhook">Authorized Slack incoming webhook URL</label>
          <input id="slack-webhook" name="slackWebhook" type="password" class="form-control" autocomplete="new-password" [value]="webhook" (input)="webhook = $any($event.target).value" [disabled]="busy()">
          <div class="form-text mb-2">Official HTTPS hooks.slack.com/services/ URL only. Leave blank to retain saved webhook. Do not paste the webhook into a subscription.</div>
           <div class="form-check mb-2"><input id="slack-enabled" name="slackEnabled" type="checkbox" class="form-check-input" [checked]="enabled" (change)="enabled = $any($event.target).checked" [disabled]="busy()"><label for="slack-enabled" class="form-check-label">Automatic Slack delivery (on as soon as settings are saved)</label></div>
          <button data-slack-save type="submit" class="btn btn-primary" [disabled]="busy()">Save Slack settings</button>
        </form>
        <form class="mt-3" (submit)="$event.preventDefault(); test()" novalidate>
          <div class="form-check mb-2"><input id="slack-consent" name="slackConsent" type="checkbox" class="form-check-input" [checked]="consent" (change)="consent = $any($event.target).checked" [disabled]="busy() || !status()?.enabled"><label for="slack-consent" class="form-check-label">I authorize sending one test message to the saved webhook's configured Slack channel.</label></div>
          <button data-slack-test type="submit" class="btn btn-outline-primary" [disabled]="busy() || !status()?.enabled || !consent">Send one test to Slack</button>
        </form>
        <div role="status" aria-live="polite" class="mt-2">@if (message()) { <p class="alert alert-success">{{ message() }}</p> } @if (error()) { <p class="alert alert-danger">{{ error() }}</p> }</div>
      }
    </div></section>
  `,
})
export class SlackSetupComponent {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly access = signal<'loading' | 'login' | 'admin' | 'error'>('loading');
  protected readonly status = signal<SlackStatus | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');
  protected webhook = '';
  protected enabled = true;
  protected label = '';
  protected consent = false;

  constructor() {
    this.http.get<SlackStatus>('/api/admin/slack').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: status => { this.status.set(status); this.enabled = status.enabled || !status.configured;
        this.label = status.channelLabel ?? ''; this.access.set('admin'); },
      error: response => this.access.set(response.status === 401 || response.status === 403 ? 'login' : 'error'),
    });
  }

  protected save(): void {
    const status = this.status();
    if (!status || this.busy()) return;
    this.busy.set(true); this.message.set(''); this.error.set('');
    this.http.put<SlackStatus>('/api/admin/slack', {webhookUrl: this.webhook, enabled: this.enabled, channelLabel: this.label},
      {headers: {'X-CSRF-TOKEN': status.csrfToken}}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: saved => {
        this.webhook = ''; this.consent = false; this.busy.set(false);
        this.status.set({...saved, csrfToken: status.csrfToken});
        this.message.set(saved.enabled ? 'Slack settings saved and enabled.' : 'Slack settings saved; automatic delivery disabled.');
      },
      error: response => {
        this.webhook = ''; this.consent = false; this.busy.set(false);
        this.error.set(response.status === 400 && typeof response.error?.error === 'string'
          ? response.error.error : 'Could not save Slack settings. Check your admin login and try again.');
      },
    });
  }

  protected test(): void {
    const status = this.status();
    if (!status?.enabled || !this.consent || this.busy()) return;
    this.consent = false; this.busy.set(true); this.message.set(''); this.error.set('');
    this.http.post<{message: string}>('/api/admin/slack/test', {consent: true},
      {headers: {'X-CSRF-TOKEN': status.csrfToken}}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.message.set('Slack accepted the test message. Check the configured channel.'); },
      error: () => { this.busy.set(false); this.error.set('Slack did not accept the test message. Check settings and try again if appropriate.'); },
    });
  }
}
