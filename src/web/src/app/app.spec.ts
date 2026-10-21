import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from './app';
import { appRoutes } from './app.routes';

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(appRoutes)],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createApp() {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    return fixture;
  }

  function statusText(fixture: ReturnType<typeof createApp>): string {
    const element = fixture.nativeElement.querySelector('[data-testid="backend-status"]');
    return element?.textContent ?? '';
  }

  it('renders the World Alerts shell', () => {
    const fixture = createApp();

    expect(fixture.nativeElement.querySelector('h1')?.textContent).toContain('World Alerts');

    httpMock.expectOne('/api/health').flush({ status: 'ok' });
  });

  it('exposes a navigable admin route without replacing the existing shell', async () => {
    const fixture = createApp();
    httpMock.expectOne('/api/health').flush({ status: 'ok' });
    await TestBed.inject(Router).navigateByUrl('/admin');
    fixture.detectChanges();
    httpMock.expectOne('/api/admin/status').flush({
      sources: [{ sourceId: 'news', health: 'unavailable', pollStatus: 'poll failed', lastPollUtc: null }],
      delivery: { status: 'available', queued: 3, failed: 1 },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="backend-status"]')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('poll failed');
    expect(fixture.nativeElement.textContent).toContain('queued: 3');
    expect(fixture.nativeElement.querySelector('form[data-testid="admin-login"]')).toBeNull();
  });

  it('navigates to alert setup while retaining news and admin navigation', async () => {
    const fixture = createApp();
    httpMock.expectOne('/api/health').flush({ status: 'ok' });
    const nav: HTMLElement = fixture.nativeElement.querySelector('nav[aria-label="Site navigation"]');
    expect(nav.querySelector('a[routerLink="/"]')).toBeTruthy();
    expect(nav.querySelector('a[routerLink="/admin"]')).toBeTruthy();
    const setupLink = nav.querySelector<HTMLAnchorElement>('a[routerLink="/subscriptions"]');
    expect(setupLink?.textContent).toContain('Alert setup');

    await TestBed.inject(Router).navigateByUrl('/subscriptions');
    fixture.detectChanges();
    httpMock.expectOne('/api/subscriptions').flush([]);
    httpMock.expectOne('/api/admin/email').flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#subscriptions-heading')?.textContent).toContain('Alert setup');
  });

  it('reports connecting while the backend health check is pending', () => {
    const fixture = createApp();

    expect(statusText(fixture)).toContain('Connecting');

    httpMock.expectOne('/api/health').flush({ status: 'ok' });
  });

  it('reports the backend as available when /api/health succeeds', async () => {
    const fixture = createApp();

    const request = httpMock.expectOne('/api/health');
    expect(request.request.method).toBe('GET');
    request.flush({ status: 'ok' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(statusText(fixture)).toContain('Available');
  });

  it('reports the backend as unavailable when /api/health fails', async () => {
    const fixture = createApp();

    httpMock
      .expectOne('/api/health')
      .flush('unavailable', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(statusText(fixture)).toContain('Unavailable');
  });
});
