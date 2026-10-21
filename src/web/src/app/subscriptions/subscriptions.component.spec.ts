import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SubscriptionsComponent } from './subscriptions.component';

describe('Alert setup', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SubscriptionsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function page(items: unknown[] = []) {
    const fixture = TestBed.createComponent(SubscriptionsComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Loading');
    http.expectOne('/api/subscriptions').flush(items);
    http.expectOne('/api/admin/email').flush(null, {status: 401, statusText: 'Unauthorized'});
    http.expectOne('/api/alert-settings/types').flush({isCustomized: false, csrfToken: 'type-csrf', selectedTypes: [
      'world-news', 'conflict', 'politics', 'diplomacy', 'disaster.earthquake', 'disaster.flood',
      'disaster.cyclone', 'disaster.wildfire', 'disaster.volcano', 'disaster.drought',
      'disaster.tsunami', 'disaster.landslide', 'disaster.epidemic', 'market.crypto', 'market.fx',
      'market.indices', 'health.humanitarian', 'tech.cyber', 'tech.ai', 'tech.acquisitions',
      'tech.critical-infrastructure', 'environment.energy', 'science.transformative', 'economy.financial']});
    http.expectOne('/api/admin/slack').flush(null, {status: 401, statusText: 'Unauthorized'});
    http.expectOne('/api/admin/webhooks').flush(null, {status: 401, statusText: 'Unauthorized'});
    fixture.detectChanges();
    return fixture;
  }

  it('shows compact selected tags and saves the edited selection once for every channel', () => {
    const fixture = page();
    const tags = fixture.nativeElement.querySelectorAll('[data-event-tag]') as NodeListOf<HTMLButtonElement>;
    expect(tags.length).toBe(24);
    expect([...tags].every(tag => tag.getAttribute('aria-pressed') === 'true')).toBe(true);
    tags[0].click(); fixture.detectChanges();
    expect(tags[0].getAttribute('aria-pressed')).toBe('false');
    (fixture.nativeElement.querySelector('[data-save-types]') as HTMLButtonElement).click();
    const save = http.expectOne('/api/alert-settings/types');
    expect(save.request.method).toBe('PUT');
    expect(save.request.headers.get('X-CSRF-TOKEN')).toBe('type-csrf');
    expect(save.request.body.selectedTypes).not.toContain('world-news');
    save.flush({isCustomized: true, selectedTypes: save.request.body.selectedTypes});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Saved event types');
  });

  it('does not allow editing tags before the saved preference has loaded', () => {
    const fixture = TestBed.createComponent(SubscriptionsComponent);
    fixture.detectChanges();
    const tags = fixture.nativeElement.querySelectorAll('[data-event-tag]') as NodeListOf<HTMLButtonElement>;
    expect(tags.length).toBe(24);
    expect(tags[0].disabled).toBe(true);
    http.expectOne('/api/subscriptions').flush([]);
    http.expectOne('/api/admin/email').flush(null, {status: 401, statusText: 'Unauthorized'});
    http.expectOne('/api/admin/slack').flush(null, {status: 401, statusText: 'Unauthorized'});
    http.expectOne('/api/admin/webhooks').flush(null, {status: 401, statusText: 'Unauthorized'});
    http.expectOne('/api/alert-settings/types').flush({isCustomized: false, selectedTypes: ['world-news'], csrfToken: 'csrf'});
    fixture.detectChanges();
    expect(tags[0].disabled).toBe(false);
    expect(tags[0].getAttribute('aria-pressed')).toBe('true');
  });

  it('shows admin login guidance rather than SMTP credentials to a visitor', () => {
    const fixture = page();
    expect(fixture.nativeElement.textContent).toContain('Admin login');
    expect(fixture.nativeElement.querySelector('[name="smtpPassword"]')).toBeNull();
  });

  it('keeps Slack and webhook setup available when email settings fail', () => {
    const fixture = TestBed.createComponent(SubscriptionsComponent);
    fixture.detectChanges();
    http.expectOne('/api/subscriptions').flush([]);
    http.expectOne('/api/admin/email').flush(null, {status: 503, statusText: 'Unavailable'});
    http.expectOne('/api/alert-settings/types').flush({isCustomized: false, selectedTypes: [], csrfToken: 'csrf'});
    fixture.detectChanges();
    http.expectOne('/api/admin/slack').flush({configured: false, enabled: false,
      configId: 'default', target: 'not configured', csrfToken: 'slack-csrf'});
    http.expectOne('/api/admin/webhooks').flush({channels: [], csrfToken: 'webhook-csrf'});
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[name="slackWebhook"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[name="webhookId"]')).not.toBeNull();
  });

  it('saves private SMTP configuration with CSRF and explicitly tests a chosen recipient', async () => {
    const fixture = TestBed.createComponent(SubscriptionsComponent);
    fixture.detectChanges();
    http.expectOne('/api/subscriptions').flush([]);
    http.expectOne('/api/admin/email').flush({configured: false, enabled: false, host: null, port: null,
      tlsMode: null, fromAddress: null, hasCredentials: false, csrfToken: 'csrf-test'});
    http.expectOne('/api/alert-settings/types').flush({isCustomized: false, csrfToken: 'type-csrf', selectedTypes: []});
    http.expectOne('/api/admin/slack').flush(null, {status: 401, statusText: 'Unauthorized'});
    http.expectOne('/api/admin/webhooks').flush({channels: [], csrfToken: 'webhook-csrf'});
    fixture.detectChanges();
    await select(fixture, 'smtpHost', 'mail.example.org');
    await select(fixture, 'smtpUsername', 'sender');
    await select(fixture, 'smtpPassword', 'private-secret');
    await select(fixture, 'smtpFrom', 'alerts@example.org');
    await select(fixture, 'smtpRecipient', 'receiver@example.org');
    (fixture.nativeElement.querySelector('#smtp-heading')!.closest('section')!.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    const save = http.expectOne('/api/admin/email');
    expect(save.request.method).toBe('PUT');
    expect(save.request.headers.get('X-CSRF-TOKEN')).toBe('csrf-test');
    expect(save.request.body.password).toBe('private-secret');
    save.flush({configured: true, enabled: true, host: 'mail.example.org', port: 587,
      tlsMode: 'StartTls', fromAddress: 'alerts@example.org', hasCredentials: true});
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[name="smtpPassword"]').value).toBe('');
    expect(fixture.nativeElement.textContent).not.toContain('private-secret');
    await select(fixture, 'smtpTest', 'receiver@example.org');
    (fixture.nativeElement.querySelector('#smtp-test')!.closest('form')!.querySelector('button') as HTMLButtonElement).click();
    const send = http.expectOne('/api/admin/email/test');
    expect(send.request.body).toEqual({destination: 'receiver@example.org'});
    expect(send.request.headers.get('X-CSRF-TOKEN')).toBe('csrf-test');
    send.flush({message: 'SMTP accepted the test email. Inbox delivery is not guaranteed.'});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('SMTP accepted');
  });

  async function select(fixture: ReturnType<typeof page>, name: string, value: string) {
    const input = fixture.nativeElement.querySelector(`[name="${name}"]`) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('lists only approved event types and explains eligibility and current delivery limitations', () => {
    const fixture = page();
    const text = fixture.nativeElement.textContent;
    expect(fixture.nativeElement.querySelectorAll('option[data-event-type]').length).toBe(24);
    expect(text).toContain('Earthquake');
    expect(text).toContain('World news');
    expect(text).toContain('Economy and financial news');
    expect(text).toContain('24 hours');
    expect(text).toContain('not proven');
    expect(text).toContain('No subscriptions');
  });

  it('saves an enabled email subscription and upserts the same event/channel without duplicates', async () => {
    const fixture = page();
    await select(fixture, 'eventType', 'disaster.earthquake');
    await select(fixture, 'channel', 'email');
    await select(fixture, 'destination', 'person@example.org');
    const save = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    save.click();
    fixture.detectChanges();
    expect(save.disabled).toBe(true);
    const request = http.expectOne('/api/subscriptions');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({eventType: 'disaster.earthquake', channel: 'email', destination: 'person@example.org', enabled: true});
    request.flush({id: 5, eventType: 'disaster.earthquake', channel: 'email', destination: 'person@example.org', enabled: true});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Saved');
    expect(fixture.nativeElement.querySelectorAll('[data-subscription]').length).toBe(1);
    expect(fixture.nativeElement.textContent).not.toContain('person@example.org');
    save.click();
    http.expectOne('/api/subscriptions').flush({id: 5, eventType: 'disaster.earthquake', channel: 'email', destination: 'person@example.org', enabled: true});
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[data-subscription]').length).toBe(1);
  });

  it('refuses invalid email and Slack webhook secrets without sending a request', async () => {
    const fixture = page();
    await select(fixture, 'eventType', 'market.fx');
    await select(fixture, 'destination', 'not-an-email');
    fixture.nativeElement.querySelector('button[type="submit"]').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('valid email');
    http.expectNone('/api/subscriptions');
    await select(fixture, 'channel', 'slack');
    await select(fixture, 'destination', 'https://hooks.slack.com/services/secret');
    fixture.nativeElement.querySelector('button[type="submit"]').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('reference');
    expect(fixture.nativeElement.textContent).not.toContain('https://hooks.slack.com/services/secret');
    http.expectNone('/api/subscriptions');
  });

  it('saves only a non-secret Slack channel reference as a paused subscription', async () => {
    const fixture = page();
    await select(fixture, 'eventType', 'disaster.flood');
    await select(fixture, 'channel', 'slack');
    await select(fixture, 'destination', 'channel:#private-team');
    const enabled = fixture.nativeElement.querySelector('[name="enabled"]') as HTMLInputElement;
    enabled.click();
    fixture.detectChanges();
    fixture.nativeElement.querySelector('button[type="submit"]').click();
    const request = http.expectOne('/api/subscriptions');
    expect(request.request.body).toEqual({eventType: 'disaster.flood', channel: 'slack', destination: 'channel:#private-team', enabled: false});
    request.flush({id: 10, eventType: 'disaster.flood', channel: 'slack', destination: 'channel:#private-team', enabled: false});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Paused');
    expect(fixture.nativeElement.textContent).not.toContain('channel:#private-team');
  });

  it('hides destinations from list and editable fields, while allowing a disabled subscription update', async () => {
    const fixture = page([{id: 7, eventType: 'market.fx', channel: 'slack', destination: 'channel:#private', enabled: true}]);
    expect(fixture.nativeElement.textContent).not.toContain('channel:#private');
    const destination = fixture.nativeElement.querySelector('[name="destination"]') as HTMLInputElement;
    expect(destination.type).toBe('email');
    expect(destination.value).toBe('');
    (fixture.nativeElement.querySelector('[data-subscription] button[data-edit]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(destination.value).toBe('');
    expect(fixture.nativeElement.textContent).toContain('replace');
    const enabled = fixture.nativeElement.querySelector('[name="enabled"]') as HTMLInputElement;
    enabled.click();
    await fixture.whenStable();
    fixture.detectChanges();
    fixture.nativeElement.querySelector('button[type="submit"]').click();
    const request = http.expectOne('/api/subscriptions');
    expect(request.request.body).toEqual({eventType: 'market.fx', channel: 'slack', destination: 'channel:#private', enabled: false});
    request.flush({id: 7, eventType: 'market.fx', channel: 'slack', destination: 'channel:#private', enabled: false});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Paused');
    expect(fixture.nativeElement.textContent).not.toContain('channel:#private');
  });

  it('reports load and save errors without echoing private server text', async () => {
    const fixture = TestBed.createComponent(SubscriptionsComponent);
    fixture.detectChanges();
    http.expectOne('/api/admin/email').flush(null, {status: 401, statusText: 'Unauthorized'});
    http.expectOne('/api/alert-settings/types').flush({isCustomized: false, csrfToken: 'type-csrf', selectedTypes: []});
    http.expectOne('/api/admin/slack').flush(null, {status: 401, statusText: 'Unauthorized'});
    http.expectOne('/api/admin/webhooks').flush(null, {status: 401, statusText: 'Unauthorized'});
    http.expectOne('/api/subscriptions').flush('secret-error', {status: 503, statusText: 'Unavailable'});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Unable to load');
    fixture.nativeElement.querySelector('[data-retry]').click();
    http.expectOne('/api/subscriptions').flush([]);
    fixture.detectChanges();
    await select(fixture, 'eventType', 'world-news');
    await select(fixture, 'destination', 'user@example.org');
    fixture.nativeElement.querySelector('button[type="submit"]').click();
    http.expectOne('/api/subscriptions').flush('secret-error', {status: 500, statusText: 'Error'});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Unable to save');
    expect(fixture.nativeElement.textContent).not.toContain('secret-error');
  });

  it('confirms before deletion and reports deletion errors', () => {
    const fixture = page([{id: 8, eventType: 'world-news', channel: 'email', destination: 'user@example.org', enabled: true}]);
    const deleteButton = fixture.nativeElement.querySelector('[data-subscription] [data-delete]') as HTMLButtonElement;
    deleteButton.click();
    fixture.detectChanges();
    http.expectNone('/api/subscriptions/8');
    expect(fixture.nativeElement.textContent).toContain('Delete this subscription?');
    (fixture.nativeElement.querySelector('[data-confirm-delete]') as HTMLButtonElement).click();
    const request = http.expectOne('/api/subscriptions/8');
    expect(request.request.method).toBe('DELETE');
    request.flush('failed', {status: 500, statusText: 'Error'});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Unable to delete');
    (fixture.nativeElement.querySelector('[data-confirm-delete]') as HTMLButtonElement).click();
    http.expectOne('/api/subscriptions/8').flush(null, {status: 204, statusText: 'No Content'});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No subscriptions');
  });
});
