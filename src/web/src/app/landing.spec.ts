import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LandingComponent } from './landing.component';
import { appRoutes } from './app.routes';

describe('Important news landing page', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LandingComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(appRoutes)],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function page() {
    const fixture = TestBed.createComponent(LandingComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('shows only event cards by default and keeps the test action behind Debug', async () => {
    const fixture = page();
    http.expectOne('/api/news/important').flush({evaluatedUtc: '2026-10-21T12:00:00Z', omittedCount: 0,
      missingMarketBaselineCount: 4, limitations: ['Incomplete coverage'],
      items: [{title: 'News item', summary: null, sourceName: 'BBC', family: 'News', eventTypes: ['world-news'],
        link: 'https://example.org/story', provenanceUrl: 'https://example.org/feed',
        publishedUtc: '2026-10-21T11:00:00Z', importanceScore: 0.8, reasons: []}]});
    await fixture.whenStable(); fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('News item');
    expect(el.textContent).not.toContain('Displayed:');
    expect(el.textContent).not.toContain('Incomplete coverage');
    expect(el.textContent).not.toContain('market observations');
    expect(el.textContent).not.toContain('Refresh events');
    expect(el.querySelector('[data-testid="send-test-event"]')).toBeNull();
    el.querySelector<HTMLInputElement>('#score-debug')!.click(); fixture.detectChanges();
    expect(el.textContent).toContain('Displayed:');
    expect(el.textContent).toContain('Incomplete coverage');
    expect(el.querySelector('[data-testid="send-test-event"]')).not.toBeNull();
  });

  it('sends a Debug-only TEST request with a fresh token and refreshes the feed', async () => {
    const fixture = page();
    http.expectOne('/api/news/important').flush({evaluatedUtc: '2026-10-21T12:00:00Z', omittedCount: 0, items: []});
    await fixture.whenStable(); fixture.detectChanges();
    (fixture.nativeElement.querySelector('#score-debug') as HTMLInputElement).click(); fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="send-test-event"]') as HTMLButtonElement).click();
    http.expectOne('/api/test-events/token').flush({csrfToken: 'one-time-token'});
    const send = http.expectOne('/api/test-events');
    expect(send.request.method).toBe('POST');
    expect(send.request.headers.get('X-CSRF-TOKEN')).toBe('one-time-token');
    expect(send.request.body).toEqual({});
    send.flush({message: 'TEST event queued for configured matching channels.'});
    http.expectOne('/api/news/important').flush({evaluatedUtc: '2026-10-21T12:01:00Z', omittedCount: 0,
      items: [{title: 'TEST — not real news', sourceName: 'World Alerts internal test',
        link: '/subscriptions', provenanceUrl: '/subscriptions', publishedUtc: '2026-10-21T12:00:00Z',
        eventTypes: ['world-news'], reasons: ['Explicit local test request.']}]});
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('TEST event queued');
    expect(fixture.nativeElement.textContent).toContain('TEST — not real news');
  });

  it('uses the Debug companion button to show a different batch of real eligible news', async () => {
    const fixture = page();
    const items = Array.from({length: 7}, (_, index) => ({
      title: `Provider story ${index + 1}`, summary: null, family: 'News', sourceName: index % 2 ? 'BBC' : 'Guardian',
      link: `https://example.org/${index}`, provenanceUrl: `https://example.org/feed/${index % 2}`,
      publishedUtc: '2026-10-21T11:00:00Z', eventTypes: ['world-news'], reasons: [],
    }));
    http.expectOne('/api/news/important').flush({evaluatedUtc: '2026-10-21T12:00:00Z', omittedCount: 0, items});
    await fixture.whenStable(); fixture.detectChanges();
    (fixture.nativeElement.querySelector('#score-debug') as HTMLInputElement).click(); fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('[data-testid="different-news"]') as HTMLButtonElement;
    expect(button).not.toBeNull();
    expect(button.previousElementSibling?.getAttribute('data-testid')).toBe('send-test-event');
    button.click();
    http.expectOne('/api/news/important').flush({evaluatedUtc: '2026-10-21T12:01:00Z', omittedCount: 0, items});
    await fixture.whenStable(); fixture.detectChanges();
    const shown = [...fixture.nativeElement.querySelectorAll('article h3')].map((element: Element) => element.textContent?.trim());
    expect(shown).toEqual(['Provider story 6', 'Provider story 7']);
    expect(fixture.nativeElement.textContent).toContain('Showing different stored news');
  });

  it('loads live important news with provider fields, reasons and last updated time', async () => {
    const fixture = page();
    expect(fixture.nativeElement.textContent).toContain('Loading');
    const request = http.expectOne('/api/news/important');
    expect(request.request.method).toBe('GET');
    request.flush({
      evaluatedUtc: '2026-10-21T12:00:00Z', omittedCount: 0,
      items: [{ title: 'Breaking conflict', summary: 'Provider summary', sourceName: 'BBC World',
        link: 'https://example.org/story', provenanceUrl: 'https://example.org/feed',
        publishedUtc: '2026-10-21T10:00:00Z', reasons: ['Headline contains approved urgency keyword.'] }],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Breaking conflict');
    expect(el.textContent).toContain('Provider summary');
    expect(el.textContent).toContain('BBC World');
    expect(el.textContent).not.toContain('Headline contains approved urgency keyword.');
    el.querySelector<HTMLInputElement>('#score-debug')!.click();
    fixture.detectChanges();
    expect(el.textContent).toContain('Headline contains approved urgency keyword.');
    expect(el.textContent).toContain('Importance score: Not available from this API response');
    expect(el.textContent).not.toContain('Importance score: 1.00');
    expect(el.textContent).toContain('Last updated');
    expect(el.querySelector<HTMLAnchorElement>('a[href="https://example.org/story"]')).toBeTruthy();
    expect(el.querySelector<HTMLAnchorElement>('a[href="https://example.org/feed"]')).toBeTruthy();
  });

  it('shows empty state without offering manual refresh', async () => {
    const fixture = page();
    http.expectOne('/api/news/important').flush({ evaluatedUtc: '2026-10-21T12:00:00Z', omittedCount: 0, items: [] });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No important news');
    expect(fixture.nativeElement.textContent).not.toContain('Refresh events');
  });

  it('shows an error without promising manual retry', async () => {
    const fixture = page();
    http.expectOne('/api/news/important').flush('unavailable', { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Automatic refresh will retry');
  });

  it('cancels a pending request when navigating away', () => {
    const fixture = page();
    const pending = http.expectOne('/api/news/important');
    fixture.destroy();
    expect(pending.cancelled).toBe(true);
  });

  it('shows mixed families and missing baselines, keeping score details hidden until enabled', async () => {
    const fixture = page();
    http.expectOne('/api/news/important').flush({ evaluatedUtc: '2026-10-21T12:00:00Z', omittedCount: 0,
      missingMarketBaselineCount: 2, omittedRejectedCount: 3, limitations: ['Regional coverage incomplete'],
      items: [
        { title: 'Major quake', family: 'Disaster', eventTypes: ['disaster.earthquake'], summary: null,
          sourceName: 'GDACS', link: 'https://example.org/q', provenanceUrl: 'https://example.org/q',
          eventTimeUtc: '2026-10-21T10:00:00Z', publishedUtc: null,
          importanceScore: 0.8, importanceRuleId: 'disaster.gdacs-orange-red', reasons: ['Orange'] },
        { title: 'BTCUSDT at Binance', family: 'Market', eventTypes: ['market.crypto'], summary: 'BTC up 6%',
          sourceName: 'Binance', link: 'https://example.org/b', provenanceUrl: 'https://example.org/b',
          eventTimeUtc: '2026-10-21T11:00:00Z', publishedUtc: null,
          importanceScore: 0.72, importanceRuleId: 'market.move-threshold', reasons: ['6%'] },
      ] });
    await fixture.whenStable(); fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Major quake');
    expect(el.textContent).toContain('BTCUSDT at Binance');
    expect(el.textContent).not.toContain('0 news · 1 disasters · 1 markets');
    expect(el.textContent).not.toContain('2 market observations');
    expect(el.textContent).not.toContain('market.move-threshold');
    (el.querySelector<HTMLInputElement>('#score-debug')!).click(); fixture.detectChanges();
    expect(el.textContent).toContain('0 news · 1 disasters · 1 markets');
    expect(el.textContent).toContain('2 market observations');
    expect(el.textContent).toContain('market.move-threshold');
    expect(el.textContent).toContain('Importance score: 0.72');
    expect(el.textContent).toContain('6%');
  });

  it('lists every approved event type present in the displayed events', async () => {
    const fixture = page();
    http.expectOne('/api/news/important').flush({ evaluatedUtc: '2026-10-21T12:00:00Z', omittedCount: 0,
      items: [
        { title: 'Breaking conflict', family: 'News', eventTypes: ['world-news', 'conflict'], summary: null,
          sourceName: 'BBC World', link: 'https://example.org/a', provenanceUrl: 'https://example.org/a',
          publishedUtc: '2026-10-21T11:00:00Z', reasons: [] },
        { title: 'Second conflict story', family: 'News', eventTypes: ['world-news', 'conflict'], summary: null,
          sourceName: 'BBC World', link: 'https://example.org/b', provenanceUrl: 'https://example.org/b',
          publishedUtc: '2026-10-21T11:30:00Z', reasons: [] },
        { title: 'Major quake', family: 'Disaster', eventTypes: ['disaster.earthquake'], summary: null,
          sourceName: 'GDACS', link: 'https://example.org/c', provenanceUrl: 'https://example.org/c',
          publishedUtc: '2026-10-21T10:00:00Z', reasons: [] },
      ] });
    await fixture.whenStable(); fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).not.toContain('Event types in this view:');
    el.querySelector<HTMLInputElement>('#score-debug')!.click(); fixture.detectChanges();
    expect(el.textContent).toContain('Event types in this view:');
    expect(el.textContent).toContain('conflict (2)');
    expect(el.textContent).toContain('disaster.earthquake (1)');
    expect(el.textContent).toContain('world-news (2)');
  });

  it('never labels an event without an approved type code as world news', async () => {
    const fixture = page();
    http.expectOne('/api/news/important').flush({ evaluatedUtc: '2026-10-21T12:00:00Z', omittedCount: 0,
      items: [
        { title: 'Untyped observation', family: 'Market', eventTypes: [], summary: null,
          sourceName: 'Unknown venue', link: 'https://example.org/m', provenanceUrl: 'https://example.org/m',
          publishedUtc: '2026-10-21T11:00:00Z', reasons: [] },
      ] });
    await fixture.whenStable(); fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('no approved type code');
    expect(el.textContent).not.toContain('Market · world-news');
  });

  it('separates eligible but not important events from observations that could not be scored', async () => {
    const fixture = page();
    http.expectOne('/api/news/important').flush({ evaluatedUtc: '2026-10-21T12:00:00Z', omittedCount: 0,
      missingMarketBaselineCount: 10,
      familyDiagnostics: [
        { family: 'News', evaluatedCount: 270, eligibleCount: 134, importantEligibleCount: 4,
          missingBaselineCount: 0, eligibleNotImportantCount: 130, unscorableCount: 0, ruleCounts: [] },
        { family: 'Market', evaluatedCount: 40, eligibleCount: 36, importantEligibleCount: 0,
          missingBaselineCount: 10, eligibleNotImportantCount: 36, unscorableCount: 34, ruleCounts: [] },
      ],
      items: [] });
    await fixture.whenStable(); fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    el.querySelector<HTMLInputElement>('#score-debug')!.click(); fixture.detectChanges();
    expect(el.textContent).toContain('130 eligible but not important');
    expect(el.textContent).toContain('34 of those could not be scored from provider data');
    expect(el.textContent).toContain('10 eligible observations missing exact 24-hour baselines');
  });
});
