import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface ChannelStatus { id: string; label: string; enabled: boolean; configured: boolean; }
interface WebhookStatus { channels: ChannelStatus[]; csrfToken: string; }

@Component({
  selector: 'app-webhook-setup', standalone: true,
  template: `
    <section class="card mb-4" aria-labelledby="webhook-heading"><div class="card-body">
      <h3 id="webhook-heading" class="h5">Additional channel · HTTPS webhook</h3>
      <p class="small text-secondary">Connect another platform without changing this application: copy that platform's public HTTPS webhook URL and select its required JSON body. The URL and authorization value stay protected on the server; they are never shown after saving. Private-network destinations and redirects are blocked.</p>
      @if (access() === 'login') { <p>Sign in at <a href="/admin/login">Admin login</a> to configure additional channels.</p> }
      @if (access() === 'error') { <p role="alert">Webhook settings unavailable. Reload to retry.</p> }
      @if (status(); as state) {
        <form (submit)="$event.preventDefault(); save()" novalidate>
          <div class="row g-2 mb-2"><div class="col-md-4"><label class="form-label" for="webhook-id">Channel ID</label><input id="webhook-id" name="webhookId" class="form-control" [value]="id" (input)="id = $any($event.target).value" placeholder="ops" autocomplete="off"></div>
          <div class="col-md-8"><label class="form-label" for="webhook-label">Readable label</label><input id="webhook-label" name="webhookLabel" class="form-control" [value]="label" (input)="label = $any($event.target).value" placeholder="Operations" autocomplete="off"></div></div>
          <label class="form-label" for="webhook-url">Public HTTPS webhook URL (secret)</label><input id="webhook-url" name="webhookUrl" type="password" class="form-control mb-2" [value]="url" (input)="url = $any($event.target).value" autocomplete="new-password" placeholder="https://…">
          <label class="form-label" for="webhook-method">HTTP method</label><select id="webhook-method" name="webhookMethod" class="form-select mb-2" [value]="method" (change)="method = $any($event.target).value"><option>POST</option><option>PUT</option></select>
          <label class="form-label" for="webhook-template">JSON body (use {{'{{title}}'}} and {{'{{body}}'}})</label><textarea id="webhook-template" name="webhookTemplate" class="form-control mb-2" rows="2" [value]="jsonTemplate" (input)="jsonTemplate = $any($event.target).value"></textarea>
          <label class="form-label" for="webhook-authorization">Authorization header value (optional secret)</label><input id="webhook-authorization" name="headerValue" type="password" class="form-control mb-2" [value]="headerValue" (input)="headerValue = $any($event.target).value" autocomplete="new-password" placeholder="Bearer …">
          <div class="form-check mb-2"><input id="webhook-enabled" type="checkbox" class="form-check-input" [checked]="enabled" (change)="enabled = $any($event.target).checked"><label for="webhook-enabled" class="form-check-label">Automatic delivery after saving</label></div>
          <button data-webhook-save type="submit" class="btn btn-primary" [disabled]="busy()">Save webhook channel</button>
        </form>
        <div role="status" class="mt-2 small">{{ message() }}</div>
        @if (state.channels.length) {
          <div class="d-flex flex-wrap gap-2 mt-3">@for (channel of state.channels; track channel.id) {
            <span class="badge text-bg-secondary">{{ channel.label }} · {{ channel.enabled ? 'ready' : 'paused' }}</span>
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="test(channel.id)" [disabled]="busy() || !channel.enabled">Send one test to {{ channel.label }}</button>
          }</div>
        }
      }
    </div></section>
  `,
})
export class WebhookSetupComponent {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly status = signal<WebhookStatus | null>(null);
  protected readonly access = signal<'loading' | 'login' | 'admin' | 'error'>('loading');
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected id = '';
  protected label = '';
  protected url = '';
  protected method = 'POST';
  protected jsonTemplate = '{"text":"{{title}}"}';
  protected headerValue = '';
  protected enabled = true;

  constructor() {
    this.http.get<WebhookStatus>('/api/admin/webhooks').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => { this.status.set(result); this.access.set('admin'); },
      error: error => this.access.set(error.status === 401 || error.status === 403 ? 'login' : 'error'),
    });
  }

  protected save(): void {
    const status = this.status();
    if (!status || this.busy()) return;
    this.busy.set(true); this.message.set('');
    this.http.put<ChannelStatus>('/api/admin/webhooks', {
      id: this.id.trim(), label: this.label.trim(), url: this.url, method: this.method,
      headers: this.headerValue ? {Authorization: this.headerValue} : {},
      jsonTemplate: this.jsonTemplate, enabled: this.enabled,
    }, {headers: {'X-CSRF-TOKEN': status.csrfToken}}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: channel => {
        this.url = ''; this.headerValue = ''; this.busy.set(false);
        this.status.set({...status, channels: [...status.channels.filter(existing => existing.id !== channel.id), channel]});
        this.message.set('Webhook channel saved. Eligible alerts can now be delivered automatically.');
      },
      error: response => {
        this.url = ''; this.headerValue = ''; this.busy.set(false);
        this.message.set(response.status === 400 && typeof response.error?.error === 'string'
          ? response.error.error : 'Could not save webhook channel; check your login and settings.');
      },
    });
  }

  protected test(id: string): void {
    const status = this.status();
    if (!status || this.busy() || !window.confirm('Send one TEST message to this configured webhook?')) return;
    this.busy.set(true);
    this.http.post<{message: string}>('/api/admin/webhooks/test', {id, consent: true},
      {headers: {'X-CSRF-TOKEN': status.csrfToken}}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: result => { this.message.set(result.message); this.busy.set(false); },
        error: () => { this.message.set('Webhook did not accept the test. No automatic retry of this test occurred.'); this.busy.set(false); },
      });
  }
}
