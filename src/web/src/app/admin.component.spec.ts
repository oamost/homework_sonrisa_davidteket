import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminComponent } from '../../admin/admin.component';

describe('AdminComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('does not present unauthenticated data as a healthy or empty queue', async () => {
    const fixture = TestBed.createComponent(AdminComponent);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/admin/status').flush('', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Sign in to view operational status');
    expect(fixture.nativeElement.querySelector('input[type="password"]')).toBeTruthy();
    expect(fixture.nativeElement.textContent).not.toContain('queued: 0');
  });

  it('renders only server-projected operational fields, not browser-stored credentials', async () => {
    const fixture = TestBed.createComponent(AdminComponent);
    fixture.detectChanges();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/status');
    expect(request.request.withCredentials).toBe(false);
    request.flush({
      sources: [{ sourceId: 'market', health: 'unavailable', pollStatus: 'unavailable', lastPollUtc: null }],
      delivery: { status: 'unavailable', queued: null, failed: null },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('queued: unavailable');
    expect(fixture.nativeElement.textContent).toContain('failed: unavailable');
  });

  it('uses a same-origin server-issued CSRF token to sign out and removes the private snapshot', async () => {
    const fixture = TestBed.createComponent(AdminComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/admin/status').flush({ sources: [], delivery: { status: 'available', queued: 4, failed: 0 } });
    await fixture.whenStable();
    fixture.detectChanges();
    fixture.nativeElement.querySelector('button[data-testid="admin-signout"]').click();
    http.expectOne('/admin/login').flush('<form><input type="hidden" name="csrf" value="server-issued-token"></form>');
    const logout = http.expectOne('/admin/logout');
    expect(logout.request.method).toBe('POST');
    expect(logout.request.body.toString()).toContain('csrf=server-issued-token');
    logout.flush(null);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('queued: 4');
    expect(fixture.nativeElement.querySelector('input[type="password"]')).toBeTruthy();
  });

  it('logs in with an antiforgery token and clears the password field, then loads actual status', async () => {
    const fixture = TestBed.createComponent(AdminComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/admin/status').flush('', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    const password = fixture.nativeElement.querySelector('input[type="password"]') as HTMLInputElement;
    password.value = 'ephemeral-password';
    fixture.nativeElement.querySelector('form[data-testid="admin-login"]').dispatchEvent(new Event('submit', { cancelable: true }));
    expect(password.value).toBe('');
    http.expectOne('/admin/login').flush('<form><input name="csrf" value="csrf-value"></form>');
    const request = http.expectOne('/admin/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.toString()).toContain('csrf=csrf-value');
    expect(request.request.body.toString()).toContain('password=ephemeral-password');
    request.flush('', { status: 200, statusText: 'OK' });
    http.expectOne('/api/admin/status').flush({ sources: [], delivery: { status: 'available', queued: 1, failed: 0 } });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('queued: 1');
    expect(fixture.nativeElement.textContent).not.toContain('ephemeral-password');
  });
});
