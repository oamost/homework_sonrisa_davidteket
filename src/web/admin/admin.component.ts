import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface SourceStatus {
  sourceId: string;
  health: string;
  pollStatus: string;
  lastPollUtc: string | null;
}

interface Operations {
  sources: SourceStatus[];
  delivery: { status: string; queued: number | null; failed: number | null };
}

// Standalone Angular view: parent may embed it in the existing app. The API
// enforces auth independently of the frontend; never put credentials in JS.
@Component({
  selector: 'app-admin',
  standalone: true,
  template: `
    <section aria-label="Admin operations" class="mx-auto" style="max-width: 1080px">
      <div class="d-flex flex-wrap justify-content-between align-items-center gap-3 mb-4">
        <div><p class="small text-uppercase fw-semibold text-primary mb-1">Administration</p>
          <h1 class="h2 fw-bold mb-1">Operations overview</h1>
          <p class="text-secondary mb-0">Source polling and delivery at a glance.</p></div>
        @if (operations()) { <button type="button" class="btn btn-outline-secondary" data-testid="admin-signout" (click)="signOut()">Sign out</button> }
      </div>
      @if (operations() && stale()) { <p data-testid="admin-stale" class="alert alert-warning" role="status">Status may be stale. Last updated: {{ lastUpdated() }}. Automatic refresh will retry.</p> }
      @if (operations(); as state) {
        <div class="row g-3 mb-4">
          <div class="col-12 col-sm-4"><div class="card border-0 shadow-sm h-100"><div class="card-body p-4"><h2 class="h6 text-secondary">Delivery status</h2><p class="h4 mb-0">{{ state.delivery.status }}</p></div></div></div>
          <div class="col-6 col-sm-4"><div class="card border-0 shadow-sm h-100"><div class="card-body p-4"><h2 class="h6 text-secondary">Queued</h2><p class="h4 mb-0">queued: {{ state.delivery.queued ?? 'unavailable' }}</p></div></div></div>
          <div class="col-6 col-sm-4"><div class="card border-0 shadow-sm h-100"><div class="card-body p-4"><h2 class="h6 text-secondary">Failed sends</h2><p class="h4 mb-0">failed: {{ state.delivery.failed ?? 'unavailable' }}</p></div></div></div>
        </div>
        <div class="card border-0 shadow-sm"><div class="card-body p-4">
          <h2 class="h5 mb-3">Source health and polling</h2>
          <div class="table-responsive"><table class="table table-hover align-middle mb-0"><thead class="table-light"><tr><th scope="col">Source</th><th scope="col">Health</th><th scope="col">Poll status</th><th scope="col">Last poll UTC</th></tr></thead>
            <tbody>@for (source of state.sources; track source.sourceId) {
              <tr><th scope="row">{{ source.sourceId }}</th><td>{{ source.health }}</td><td>{{ source.pollStatus }}</td><td>{{ source.lastPollUtc ?? 'unavailable' }}</td></tr>
            } @empty { <tr><td colspan="4" class="text-secondary">No source records reported. Check the runtime reader.</td></tr> }</tbody>
          </table></div>
        </div></div>
      } @else {
        <div class="card border-0 shadow-sm" style="max-width: 480px"><div class="card-body p-4 p-md-5">
          <h2 class="h4 fw-semibold mb-3">Admin access</h2>
          <p class="text-secondary" role="status">{{ message() }}</p>
          @if (loginAvailable()) {
            <form data-testid="admin-login" (submit)="signIn($event, passwordField)">
              <label class="form-label" for="admin-password">Local admin password</label>
              <input #passwordField class="form-control mb-3" id="admin-password" type="password" autocomplete="current-password" required>
              <button type="submit" class="btn btn-primary w-100">Sign in</button>
            </form>
          }
        </div></div>
      }
    </section>
  `,
})
export class AdminComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refreshTimer: ReturnType<typeof setInterval>;
  private refreshing = false;
  private authenticated = true;
  protected readonly stale = signal(false);
  protected readonly lastUpdated = signal('');
  protected readonly operations = signal<Operations | null>(null);
  protected readonly message = signal('Loading operational status…');
  protected readonly loginAvailable = signal(false);

  constructor() {
    this.refreshTimer = setInterval(() => {
      if (this.authenticated && document.visibilityState !== 'hidden') this.refreshStatus();
    }, 30_000);
    this.destroyRef.onDestroy(() => clearInterval(this.refreshTimer));
  }

  ngOnInit(): void { this.refreshStatus(); }

  private refreshStatus(): void {
    if (this.refreshing || !this.authenticated) return;
    this.refreshing = true;
    this.http.get<Operations>('/api/admin/status').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: snapshot => {
        this.operations.set(snapshot);
        this.lastUpdated.set(new Date().toLocaleTimeString());
        this.stale.set(false);
        this.loginAvailable.set(false);
        this.refreshing = false;
      },
      error: (error: HttpErrorResponse) => {
        this.refreshing = false;
        if (error.status === 401 || error.status === 403) {
          this.authenticated = false;
          this.operations.set(null);
          this.stale.set(false);
          this.loginAvailable.set(true);
          this.message.set('Sign in to view operational status.');
        } else if (this.operations()) {
          this.stale.set(true);
        } else {
          this.message.set('Operational status unavailable. The backend may be offline.');
        }
      },
    });
  }

  protected signIn(event: Event, passwordField: HTMLInputElement): void {
    event.preventDefault();
    const password = passwordField.value;
    passwordField.value = '';
    this.http.get('/admin/login', { responseType: 'text' }).subscribe({
      next: html => {
        const token = this.csrfFrom(html);
        if (!token) { this.message.set('Sign in unavailable. Please try again.'); return; }
        this.http.post('/admin/login', new HttpParams().set('csrf', token).set('password', password), { responseType: 'text' }).subscribe({
          next: () => {
            this.http.get<Operations>('/api/admin/status').subscribe({
              next: snapshot => { this.operations.set(snapshot); this.authenticated = true; this.stale.set(false); this.lastUpdated.set(new Date().toLocaleTimeString()); this.loginAvailable.set(false); },
              error: () => this.message.set('Operational status unavailable. Please try again.'),
            });
          },
          error: () => this.message.set('Sign in failed. Check the password and server configuration.'),
        });
      },
      error: () => this.message.set('Sign in unavailable. The backend may be offline.'),
    });
  }

  protected signOut(): void {
    // The server issues a fresh antiforgery request token in its login form.
    // No password or token is persisted in the browser; cookies remain HttpOnly.
    this.http.get('/admin/login', { responseType: 'text' }).subscribe({
      next: html => {
        const token = this.csrfFrom(html);
        if (!token) {
          this.message.set('Sign out unavailable. Please try again.');
          return;
        }
        this.http.post('/admin/logout', new HttpParams().set('csrf', token), { responseType: 'text' }).subscribe({
          next: () => {
            this.operations.set(null);
            this.authenticated = false;
            this.stale.set(false);
            this.loginAvailable.set(true);
            this.message.set('Signed out.');
          },
          error: () => this.message.set('Sign out unavailable. Please try again.'),
        });
      },
      error: () => this.message.set('Sign out unavailable. Please try again.'),
    });
  }

  private csrfFrom(html: string): string | undefined {
    return new DOMParser().parseFromString(html, 'text/html')
      .querySelector<HTMLInputElement>('input[name="csrf"]')?.value;
  }
}
