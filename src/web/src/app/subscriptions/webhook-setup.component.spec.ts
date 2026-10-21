import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WebhookSetupComponent } from './webhook-setup.component';

describe('Generic webhook setup', () => {
  it('adds a channel from configuration without disclosing its URL or token', () => {
    TestBed.configureTestingModule({imports: [WebhookSetupComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]});
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(WebhookSetupComponent);
    fixture.detectChanges();
    http.expectOne('/api/admin/webhooks').flush({channels: [], csrfToken: 'csrf'});
    fixture.detectChanges();
    const input = (name: string, value: string) => {
      const element = fixture.nativeElement.querySelector(`[name="${name}"]`) as HTMLInputElement;
      element.value = value; element.dispatchEvent(new Event('input'));
    };
    input('webhookId', 'ops'); input('webhookLabel', 'Operations');
    input('webhookUrl', 'https://example.org/secret-path');
    input('headerValue', 'private-token');
    (fixture.nativeElement.querySelector('[data-webhook-save]') as HTMLButtonElement).click();
    const save = http.expectOne('/api/admin/webhooks');
    expect(save.request.method).toBe('PUT');
    expect(save.request.body.enabled).toBe(true);
    expect(save.request.body.headers.Authorization).toBe('private-token');
    save.flush({id: 'ops', label: 'Operations', enabled: true, configured: true});
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Operations');
    expect(fixture.nativeElement.textContent).not.toContain('private-token');
    expect(fixture.nativeElement.textContent).not.toContain('secret-path');
    http.verify();
  });
});
