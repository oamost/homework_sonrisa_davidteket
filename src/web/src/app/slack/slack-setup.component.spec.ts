import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SlackSetupComponent } from './slack-setup.component';

describe('Private Slack setup', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({imports: [SlackSetupComponent], providers: [provideHttpClient(), provideHttpClientTesting()]}).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('shows admin login and no webhook input to unauthenticated users', () => {
    const fixture = TestBed.createComponent(SlackSetupComponent);
    fixture.detectChanges();
    http.expectOne('/api/admin/slack').flush(null, {status: 401, statusText: 'Unauthorized'});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Admin login');
    expect(fixture.nativeElement.querySelector('[name="slackWebhook"]')).toBeNull();
  });

  it('requires enabled saved settings and explicit channel consent before sending', () => {
    const fixture = TestBed.createComponent(SlackSetupComponent);
    fixture.detectChanges();
    http.expectOne('/api/admin/slack').flush({configured: false, enabled: false, configId: 'default', target: 'not configured', csrfToken: 'csrf-test'});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('not configured');
    expect((fixture.nativeElement.querySelector('[data-slack-test]') as HTMLButtonElement).disabled).toBe(true);
    const input = fixture.nativeElement.querySelector('[name="slackWebhook"]') as HTMLInputElement;
    input.value = 'https://hooks.slack.com/services/T/B/SECRET1234';
    input.dispatchEvent(new Event('input'));
    (fixture.nativeElement.querySelector('[name="slackEnabled"]') as HTMLInputElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-slack-save]') as HTMLButtonElement).click();
    const save = http.expectOne('/api/admin/slack');
    expect(save.request.headers.get('X-CSRF-TOKEN')).toBe('csrf-test');
    expect(save.request.body).toEqual({webhookUrl: 'https://hooks.slack.com/services/T/B/SECRET1234', enabled: true});
    save.flush({configured: true, enabled: true, configId: 'default', target: 'Slack incoming webhook configured'});
    fixture.detectChanges();
    expect(input.value).toBe('');
    expect(fixture.nativeElement.textContent).not.toContain('SECRET1234');
    expect((fixture.nativeElement.querySelector('[data-slack-test]') as HTMLButtonElement).disabled).toBe(true);
    (fixture.nativeElement.querySelector('[name="slackConsent"]') as HTMLInputElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-slack-test]') as HTMLButtonElement).click();
    const send = http.expectOne('/api/admin/slack/test');
    expect(send.request.body).toEqual({consent: true});
    expect(send.request.headers.get('X-CSRF-TOKEN')).toBe('csrf-test');
    send.flush({message: 'Slack accepted the test message.'});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Slack accepted');
    expect((fixture.nativeElement.querySelector('[name="slackConsent"]') as HTMLInputElement).checked).toBe(false);
  });
});
