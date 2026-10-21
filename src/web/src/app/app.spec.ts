import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
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
