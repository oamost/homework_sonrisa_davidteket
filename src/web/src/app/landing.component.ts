import { HttpClient } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface ImportantNewsItem {
  title: string;
  summary: string | null;
  sourceName: string;
  link: string;
  provenanceUrl: string;
  publishedUtc: string;
  eventTimeUtc?: string | null;
  family?: string;
  eventTypes?: string[];
  importanceScore?: number;
  importanceRuleId?: string;
  reasons: string[];
}

interface FamilyDiagnostics {
  family: string;
  evaluatedCount: number;
  eligibleCount: number;
  importantEligibleCount: number;
  missingBaselineCount: number;
  eligibleNotImportantCount?: number;
  unscorableCount?: number;
  ruleCounts?: { ruleId: string; evaluatedCount: number; eligibleCount: number;
    importantEligibleCount: number }[];
}

interface ImportantNewsResponse {
  evaluatedUtc: string;
  items: ImportantNewsItem[];
  omittedCount: number;
  missingMarketBaselineCount?: number;
  omittedRejectedCount?: number;
  limitations?: string[];
  familyDiagnostics?: FamilyDiagnostics[];
}

interface TypeCoverage {
  code: string;
  count: number;
}

@Component({
  selector: 'app-landing',
  imports: [DatePipe],
  templateUrl: './landing.component.html',
})
export class LandingComponent {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refreshTimer: ReturnType<typeof setInterval>;

  protected readonly response = signal<ImportantNewsResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal(false);
  protected readonly showScoreDebug = signal(false);

  protected toggleScoreDebug(): void { this.showScoreDebug.update(value => !value); }

  protected countFamily(items: ImportantNewsItem[], family: string): number {
    return items.filter(item => (item.family || 'News') === family).length;
  }

  protected noQualified(response: ImportantNewsResponse, family: string): boolean {
    const diagnostic = response.familyDiagnostics?.find(item => item.family === family);
    return diagnostic ? diagnostic.importantEligibleCount === 0 : this.countFamily(response.items, family) === 0;
  }

  /** Approved subscription codes present in the displayed events, with how many events carry each. */
  protected typeCoverage(items: ImportantNewsItem[]): TypeCoverage[] {
    const counts = new Map<string, number>();
    for (const item of items) {
      for (const code of item.eventTypes ?? []) {
        if (code) counts.set(code, (counts.get(code) ?? 0) + 1);
      }
    }
    return [...counts.entries()]
      .map(([code, count]) => ({ code, count }))
      .sort((a, b) => b.count - a.count || a.code.localeCompare(b.code));
  }

  /** The event's approved type codes, or an explicit marker when the provider gave no code. */
  protected typeLabel(item: ImportantNewsItem): string {
    const codes = (item.eventTypes ?? []).filter(code => !!code);
    if (codes.length) return codes.join(', ');
    return (item.family || 'News') === 'News' ? 'world-news' : 'no approved type code';
  }

  /** Eligible events that were scored and rejected, apart from events that could not be scored. */
  protected belowThreshold(diagnostic: FamilyDiagnostics): number {
    return Math.max(0, (diagnostic.eligibleNotImportantCount ?? 0) - (diagnostic.unscorableCount ?? 0));
  }

  protected scoreLabel(score: number | undefined): string {
    return typeof score === 'number' && Number.isFinite(score) && score >= 0 && score <= 1
      ? score.toFixed(2) : 'Not available from this API response';
  }

  constructor() {
    this.refresh();
    this.refreshTimer = setInterval(() => this.refresh(), 60_000);
    this.destroyRef.onDestroy(() => clearInterval(this.refreshTimer));
  }

  refresh(): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.error.set(false);
    this.http.get<ImportantNewsResponse>('/api/news/important')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.response.set(result);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        },
      });
  }
}
