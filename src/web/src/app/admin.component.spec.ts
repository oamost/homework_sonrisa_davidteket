import { vi } from 'vitest';
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

describe('AdminComponent — auto refresh (fake timers)', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    vi.useFakeTimers();
    await TestBed.configureTestingModule({
      imports: [AdminComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  function load(queued: number, failed: number) {
    const fixture = TestBed.createComponent(AdminComponent);
    fixture.detectChanges();
    http.expectOne('/api/admin/status').flush({
      sources: [],
      delivery: { status: 'available', queued, failed },
    });
    fixture.detectChanges();
    return fixture;
  }

  it('re-fetches status every 30 seconds while the page is visible', () => {
    const fixture = load(1, 0);
    expect(fixture.nativeElement.textContent).toContain('queued: 1');

    vi.advanceTimersByTime(29_000);
    http.expectNone('/api/admin/status');

    vi.advanceTimersByTime(1_000);
    const second = http.expectOne('/api/admin/status');
    second.flush({ sources: [], delivery: { status: 'available', queued: 5, failed: 1 } });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('queued: 5');
  });

  it('keeps the last successful snapshot and flags it stale when a refresh fails', () => {
    const fixture = load(7, 2);
    expect(fixture.nativeElement.textContent).toContain('queued: 7');
    expect(fixture.nativeElement.querySelector('[data-testid="admin-stale"]')).toBeNull();

    vi.advanceTimersByTime(30_000);
    http.expectOne('/api/admin/status').flush('downgrade', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="admin-stale"]')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Last updated');
    expect(fixture.nativeElement.textContent).toContain('queued: 7');
  });

  it('does not present healthy or queued data when the backend is offline on first load', () => {
    const fixture = TestBed.createComponent(AdminComponent);
    fixture.detectChanges();
    http.expectOne('/api/admin/status').flush('down', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Operational status unavailable');
    expect(fixture.nativeElement.textContent).not.toContain('queued:');
    expect(fixture.nativeElement.textContent).not.toContain('Last updated');
    expect(fixture.nativeElement.querySelector('[data-testid="admin-stale"]')).toBeNull();
  });

  it('stops auto-polling when unauthenticated instead of re-hitting the endpoint', () => {
    const fixture = TestBed.createComponent(AdminComponent);
    fixture.detectChanges();
    http.expectOne('/api/admin/status').flush('', { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Sign in to view operational status');
    expect(fixture.nativeElement.querySelector('input[type="password"]')).toBeTruthy();

    vi.advanceTimersByTime(30_000);
    http.expectNone('/api/admin/status');
  });
});
