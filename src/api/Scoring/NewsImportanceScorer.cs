using System.Globalization;
using WorldAlerts.Api.News;

namespace WorldAlerts.Api.Scoring;

/// <summary>
/// One independent source already identified by the caller as carrying the same
/// story. Deduplication is deliberately outside this scorer.
/// </summary>
/// <param name="SourceId">Source id used for independence and tier lookup.</param>
/// <param name="SourceName">Source display name used for tier lookup and factors.</param>
/// <param name="PublishedUtc">Provider publication time, or null when absent.</param>
public sealed record NewsCorroboratingSource(
    string SourceId,
    string SourceName,
    DateTimeOffset? PublishedUtc);

/// <summary>
/// Outcome of the financial-event analysis of one headline.
/// </summary>
/// <param name="Subject">The matched approved financial event subject, or null.</param>
/// <param name="Action">The matched approved consequential action, or null.</param>
/// <param name="Scale">The matched material scale marker, or null.</param>
/// <param name="Amount">The matched material amount word, or null.</param>
/// <param name="Exclusion">The exclusion marker that disqualified the headline, or null.</param>
public sealed record FinancialEventAnalysis(
    string? Subject,
    string? Action,
    string? Scale,
    string? Amount,
    string? Exclusion)
{
    /// <summary>
    /// True when the matched subject is itself a material capital-market or credit event, so a
    /// material scale marker can stand in for a consequential action.
    /// </summary>
    public bool ScaleApplies =>
        Subject is not null
        && Scale is not null
        && NewsImportanceScorer.FinancialScaleBearingSubjects.Contains(Subject, StringComparer.Ordinal);

    /// <summary>
    /// True when the matched subject is one for which a consequential action alone qualifies.
    /// </summary>
    public bool ActionApplies =>
        Subject is not null
        && Action is not null
        && NewsImportanceScorer.FinancialActionSubjects.Contains(Subject, StringComparer.Ordinal);

    /// <summary>
    /// True when the headline describes a consequential financial event: an approved subject plus
    /// an approved consequential action or a material scale, with no exclusion marker.
    /// </summary>
    public bool IsConsequentialEvent =>
        Subject is not null && Exclusion is null && (ActionApplies || ScaleApplies);
}

/// <summary>
/// Deterministic news importance scorer for the approved acceptance fixtures.
/// </summary>
/// <remarks>
/// The approved examples in
/// <c>docs/decisions/2026-10-21-task-01-acceptance-boundaries.md</c> are:
/// a tier-1 source plus a breaking/conflict/disaster keyword, or two
/// independent tier-1 sources within six hours. The six-hour window and the
/// default tier map are acceptance fixtures, not universal weights. No clock is
/// read inside this class; callers supply <c>nowUtc</c>.
/// <para>
/// Only metadata on <see cref="NewsItem"/> and the caller-supplied corroborating
/// sources is used. The caller is responsible for deciding that corroborating
/// entries describe the same story; this scorer checks independence, tier and
/// publication-time distance only. Missing or future publication times never
/// count as corroboration and are reported as explicit factors.
/// </para>
/// </remarks>
public sealed class NewsImportanceScorer
{
    /// <summary>The approved six-hour corroboration window.</summary>
    public static readonly TimeSpan DefaultCorroborationWindow = TimeSpan.FromHours(6);

    /// <summary>The approved urgency keyword families used by the fixtures.</summary>
    public static readonly IReadOnlyList<string> DefaultUrgencyKeywords = new[]
    {
        "breaking",
        "conflict",
        "war",
        "invasion",
        "airstrike",
        "air strike",
        "missile",
        "hostage",
        "coup",
        "disaster",
        "earthquake",
        "tsunami",
        "flood",
        "cyclone",
        "hurricane",
        "typhoon",
        "wildfire",
        "volcano",
        "drought",
        "landslide",
        "epidemic",
        "pandemic",
    };

    /// <summary>
    /// The approved positive financial event subjects (decision D1 addition, 2026-10-21).
    /// A subject alone never makes a story important: an approved consequential action or a
    /// material scale marker is required as well, and the reporting source must be trusted.
    /// </summary>
    public static readonly IReadOnlyList<string> FinancialEventSubjects = new[]
    {
        // Central banks and monetary policy.
        "central bank", "federal reserve", "fed", "ecb", "bank of england", "bank of japan",
        "rba", "boj", "boe", "imf", "world bank", "monetary policy", "policy rate",
        "interest rate", "rate hike", "rate cut",
        // Prices, activity and employment.
        "inflation", "consumer prices", "cpi", "gdp", "recession",
        "jobs report", "unemployment", "unemployment rate", "payrolls",
        // Credit stress and market structure.
        "default", "bank failure", "bank collapse", "insolvency", "bankruptcy", "bailout",
        "trading halt", "circuit breaker", "suspended trading",
        // Capital markets and mergers.
        "ipo", "initial public offering", "acquisition", "merger", "takeover",
    };

    /// <summary>
    /// The approved consequential financial actions. A material market move or a decision verb
    /// qualifies; a routine directional verb such as "rises" does not, so an ordinary monthly
    /// data print is not promoted.
    /// </summary>
    public static readonly IReadOnlyList<string> FinancialEventActions = new[]
    {
        "raise", "raises", "raised", "raising",
        "cut", "cuts", "cutting",
        "hike", "hikes", "hiked", "hiking",
        "lower", "lowers", "lowered", "lowering",
        "slash", "slashes", "slashed",
        "double", "doubles", "doubled",
        "triple", "triples", "tripled",
        "surge", "surges", "surged",
        "soar", "soars", "soared",
        "plunge", "plunges", "plunged",
        "collapse", "collapses", "collapsed",
        "contract", "contracts", "contracted",
        "shrink", "shrinks",
        "fail", "fails", "failed",
        "default", "defaults", "defaulted",
        "halt", "halts", "halted",
        "suspend", "suspends", "suspended",
        "approve", "approves", "approved",
        "agree", "agrees", "agreed",
        "complete", "completes", "completed",
        "announce", "announces", "announced",
        "launch", "launches", "launched",
        "bail out", "bails out", "bailed out",
        "rescue", "rescues", "rescued",
        "seize", "seizes", "seized",
    };

    /// <summary>
    /// Material scale markers. With a scale-bearing subject these stand in for a consequential
    /// action, so a quantified event qualifies even when the verb is not in the action list.
    /// </summary>
    public static readonly IReadOnlyList<string> FinancialScaleMarkers = new[]
    {
        "billion", "trillion", "biggest", "largest", "record", "records",
    };

    /// <summary>Amount words that add a further bounded strength to an important financial event.</summary>
    public static readonly IReadOnlyList<string> FinancialMaterialAmounts = new[]
    {
        "billion", "trillion",
    };

    /// <summary>
    /// The subjects for which a consequential action alone is enough, because the subject is
    /// itself a policy, price, activity, employment, credit or market-structure event.
    /// </summary>
    public static readonly IReadOnlyList<string> FinancialActionSubjects = new[]
    {
        "central bank", "federal reserve", "fed", "ecb", "bank of england", "bank of japan",
        "rba", "boj", "boe", "imf", "world bank", "monetary policy", "policy rate",
        "interest rate", "rate hike", "rate cut",
        "inflation", "consumer prices", "cpi", "gdp", "recession",
        "jobs report", "unemployment", "unemployment rate", "payrolls",
        "default", "bank failure", "bank collapse", "insolvency", "bankruptcy", "bailout",
        "trading halt", "circuit breaker", "suspended trading",
    };

    /// <summary>
    /// The subjects for which a material scale marker alone is enough. Capital-market events
    /// (an IPO, an acquisition, a merger, a takeover) are included here but not in
    /// <see cref="FinancialActionSubjects"/>, so an ordinary small fundraising or an unquantified
    /// deal is never promoted on its own.
    /// </summary>
    public static readonly IReadOnlyList<string> FinancialScaleBearingSubjects = new[]
    {
        "ipo", "initial public offering", "acquisition", "merger", "takeover",
        "central bank", "federal reserve", "fed", "ecb", "bank of england", "bank of japan",
        "rba", "boj", "boe", "imf", "world bank", "monetary policy", "policy rate",
        "interest rate", "rate hike", "rate cut",
        "inflation", "consumer prices", "cpi", "gdp", "recession",
        "jobs report", "unemployment", "unemployment rate", "payrolls",
        "default", "bank failure", "bank collapse", "insolvency", "bankruptcy", "bailout",
        "trading halt", "circuit breaker", "suspended trading",
    };

    /// <summary>
    /// Headline markers that disqualify a financial story from the rule: forecasts and odds,
    /// undecided speculation, routine market commentary, personal finance and promotion, and
    /// opinion or analysis pieces.
    /// </summary>
    public static readonly IReadOnlyList<string> FinancialExclusions = new[]
    {
        // Forecasts, odds and speculation.
        "forecast", "forecasts", "predict", "predicts", "predicted", "prediction",
        "expect", "expects", "expected to", "odds", "likely to", "analyst", "analysts",
        "warn", "warns", "warning", "outlook", "projection", "projected",
        "speculation", "speculates", "speculating", "rumour", "rumor",
        "could", "might", "set to", "poised to", "in talks", "weighing",
        // Routine market commentary and roundups.
        "round up", "markets today", "stock market today", "market wrap", "market update",
        "closing bell", "opening bell", "pre market", "what to watch", "what to know",
        "watchlist", "top stocks", "stocks to watch", "share price", "stock price",
        "closing price", "opening price", "live updates",
        // Personal finance and promotion.
        "your money", "personal finance", "should you buy", "investing in", "how to save",
        "saving tips", "retirement planning", "credit score", "best stocks to buy",
        "for beginners", "sponsored", "promoted", "advertisement", "discount", "coupon",
        "giveaway", "black friday", "promo code",
        // Opinion and analysis.
        "opinion", "analysis", "review", "explainer", "how to",
    };

    private readonly NewsSourceTiers _sourceTiers;
    private readonly TimeSpan _corroborationWindow;
    private readonly IReadOnlyList<string> _urgencyKeywords;

    /// <summary>Creates a scorer, optionally overriding the approved fixtures.</summary>
    /// <param name="sourceTiers">Source tier map; defaults to <see cref="NewsSourceTiers.Default"/>.</param>
    /// <param name="corroborationWindow">Corroboration window; defaults to six hours.</param>
    /// <param name="urgencyKeywords">Keyword list; defaults to <see cref="DefaultUrgencyKeywords"/>.</param>
    public NewsImportanceScorer(
        NewsSourceTiers? sourceTiers = null,
        TimeSpan? corroborationWindow = null,
        IEnumerable<string>? urgencyKeywords = null)
    {
        _sourceTiers = sourceTiers ?? NewsSourceTiers.Default;
        _corroborationWindow = corroborationWindow ?? DefaultCorroborationWindow;
        if (_corroborationWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(corroborationWindow), "The corroboration window must be positive.");
        }

        _urgencyKeywords = (urgencyKeywords ?? DefaultUrgencyKeywords)
            .Select(ScoringText.Normalize)
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Scores one news item against the approved rules.
    /// </summary>
    /// <param name="item">Normalized news item; only metadata is read.</param>
    /// <param name="nowUtc">Caller-supplied evaluation time used to reject future timestamps.</param>
    /// <param name="corroboratingSources">
    /// Other sources the caller has already associated with the same story.
    /// This scorer does not deduplicate stories; it only checks source
    /// independence, tier and publication-time distance.
    /// </param>
    public ImportanceDecision Score(
        NewsItem item,
        DateTimeOffset nowUtc,
        IReadOnlyList<NewsCorroboratingSource>? corroboratingSources = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        var factors = new List<string>();
        var sourceLabel = SourceLabel(item.SourceId, item.SourceName);
        var tier = _sourceTiers.ResolveTier(item.SourceId, item.SourceName);
        var isTierOne = string.Equals(tier, NewsSourceTier.TierOne, StringComparison.OrdinalIgnoreCase);
        factors.Add(isTierOne
            ? $"Source '{sourceLabel}' is tier-1."
            : $"Source '{sourceLabel}' is not tier-1 ({(tier ?? "unclassified")}).");

        string? keyword = null;
        if (string.IsNullOrWhiteSpace(item.Title))
        {
            factors.Add("Headline is missing, so no approved urgency keyword can be matched.");
        }
        else
        {
            keyword = ScoringText.FindTokenSequence(
                ScoringText.Normalize(item.Title),
                _urgencyKeywords);
            factors.Add(keyword is null
                ? "Headline contains no approved urgency keyword."
                : $"Headline contains approved urgency keyword '{keyword}'.");
        }

        if (isTierOne && keyword is not null)
        {
            var strength = 0.70 + (item.PublishedUtc is { } time && time <= nowUtc ? 0.05 : 0);
            factors.Add("Strength heuristic: tier-1 urgency 0.70; usable provider publication time adds 0.05.");
            return ImportanceDecision.Important(NewsImportanceRuleIds.TierOneUrgencyKeyword, strength, factors.ToArray());
        }

        if (HasCorroboration(item, nowUtc, corroboratingSources, isTierOne, factors))
        {
            factors.Add("Strength heuristic: independently corroborated tier-1 same-story reports 0.85.");
            return ImportanceDecision.Important(NewsImportanceRuleIds.TierOneCorroboration, 0.85, factors.ToArray());
        }

        var financial = AnalyzeFinancialEvent(item);
        if (financial.IsConsequentialEvent && isTierOne)
        {
            factors.Add(
                $"Headline reports a consequential financial event: subject '{financial.Subject}'"
                + (financial.Action is null ? string.Empty : $", action '{financial.Action}'")
                + (financial.Scale is null ? string.Empty : $", scale '{financial.Scale}'") + ".");
            factors.Add("Source is tier-1, so the approved financial event rule applies.");
            var financialStrength = 0.70;
            if (item.PublishedUtc is { } published && published <= nowUtc) financialStrength += 0.05;
            if (financial.Amount is not null) financialStrength += 0.05;
            factors.Add(
                "Strength heuristic: trusted-source financial event 0.70; a usable provider publication time "
                + "adds 0.05; an explicit material amount adds a further 0.05.");
            return ImportanceDecision.Important(
                NewsImportanceRuleIds.ConsequentialFinancialEvent, financialStrength, factors.ToArray());
        }

        var topic = ConsequentialTopic(item);
        if (topic is not null)
        {
            factors.Add($"Headline reports consequential {topic} action; topic/source alone does not qualify.");
            factors.Add("Strength heuristic: explicit consequential headline subject and action 0.65; not verified independently.");
            return ImportanceDecision.Important("news.consequential-topic", 0.65, factors.ToArray());
        }

        factors.Add(FinancialRuleNotApplied(financial));
        factors.Add("Strength heuristic: no approved trigger; source tier alone has no importance score.");
        return ImportanceDecision.NotImportant(NewsImportanceRuleIds.NoApprovedTrigger, factors.ToArray());
    }

    /// <summary>
    /// Analyses one headline for a consequential financial event. The analysis is metadata-only: it
    /// reads the headline text and never a clock, a price, a source list or a subscription.
    /// </summary>
    /// <param name="item">Normalized news item; only the headline is read.</param>
    public static FinancialEventAnalysis AnalyzeFinancialEvent(NewsItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var text = ScoringText.Normalize(item.Title);
        return new FinancialEventAnalysis(
            FindFinancialToken(text, FinancialEventSubjects),
            FindFinancialToken(text, FinancialEventActions),
            FindFinancialToken(text, FinancialScaleMarkers),
            FindFinancialToken(text, FinancialMaterialAmounts),
            FindFinancialToken(text, FinancialExclusions));
    }

    /// <summary>Explains, in one factor, why the financial event rule did not apply.</summary>
    private static string FinancialRuleNotApplied(FinancialEventAnalysis analysis)
    {
        if (analysis.Exclusion is not null)
        {
            return $"Financial event rule not applied: the headline is excluded as '{analysis.Exclusion}'.";
        }

        if (analysis.Subject is null)
        {
            return "Financial event rule not applied: no approved financial event subject in the headline.";
        }

        if (!analysis.IsConsequentialEvent)
        {
            return "Financial event rule not applied: financial subject without a consequential action "
                + "or a material scale marker.";
        }

        return "Financial event rule not applied: the source is not a trusted tier-1 source.";
    }

    /// <summary>
    /// Finds the first approved marker in the normalized headline. A marker also matches a direct
    /// inflection that follows it ("interest rates", "IPOs"), but never a longer unrelated word.
    /// </summary>
    private static string? FindFinancialToken(string normalizedText, IReadOnlyList<string> markers)
    {
        foreach (var marker in markers)
            if (ContainsApprovedToken(normalizedText, marker)) return marker;
        return null;
    }

    /// <summary>
    /// True when the normalized text contains the approved token as a whole word or with a direct
    /// inflection ("interest rates", "IPOs"), but never inside a longer unrelated word.
    /// </summary>
    /// <param name="normalizedText">Text normalized with <see cref="ScoringText.Normalize"/>.</param>
    /// <param name="token">The approved token to look for.</param>
    public static bool ContainsApprovedToken(string normalizedText, string token)
    {
        if (string.IsNullOrEmpty(normalizedText) || string.IsNullOrEmpty(token)) return false;
        if (ScoringText.ContainsTokenSequence(normalizedText, token)) return true;

        var index = 0;
        while ((index = normalizedText.IndexOf(" " + token, index, StringComparison.Ordinal)) >= 0)
        {
            var next = index + token.Length + 1;
            if (next >= normalizedText.Length) return true;
            foreach (var suffix in InflectionSuffixes)
            {
                if (!normalizedText.AsSpan(next).StartsWith(suffix.AsSpan(), StringComparison.Ordinal)) continue;
                var after = next + suffix.Length;
                if (after >= normalizedText.Length || !char.IsLetter(normalizedText[after])) return true;
            }

            index = next;
        }

        return false;
    }

    /// <summary>The direct inflections a marker may take at the end of a word.</summary>
    private static readonly string[] InflectionSuffixes = ["s", "es", "ed", "ing"];

    // Explicit headline action + topic, not a broad feed label or an isolated noun.
    // These are deterministic editorial indicators, not a probability or universal weight.
    private static string? ConsequentialTopic(NewsItem item)
    {
        var text = ScoringText.Normalize(item.Title);
        if (ScoringText.FindTokenSequence(text, ["opinion", "analysis", "review", "explainer", "how to", "advertisement", "sponsored", "promoted"]) is not null)
            return null;
        (string Topic, string[] Subject, string[] Action)[] rules =
        [
            ("politics", ["government", "president", "parliament", "election"], ["resigns", "ousted", "dissolves", "announces election", "election result"]),
            ("diplomacy", ["treaty", "sanctions", "ceasefire", "diplomatic relations"], ["signed", "imposed", "agreed", "suspended", "lifted"]),
            ("cybersecurity", ["ransomware", "cyberattack", "data breach", "hackers"], ["shuts down", "disrupts", "exposes", "breached", "compromised"]),
            ("AI", ["ai model", "artificial intelligence", "ai system"], ["banned", "mandated", "deployed nationwide", "recalls", "regulator approves"]),
            ("acquisitions", ["acquisition", "merger", "acquires"], ["completed", "approved", "blocked", "agreed", "announces"]),
            ("critical infrastructure", ["power grid", "water supply", "telecom network", "hospital network"], ["shuts down", "outage", "disrupted", "restored after"]),
            ("environment/energy", ["oil spill", "power plant", "emissions", "energy grid"], ["shuts down", "leaks", "banned", "emergency", "mandated"]),
            ("transformative science", ["clinical trial", "fusion reactor", "scientific breakthrough"], ["succeeds", "approved", "demonstrates", "achieves"]),
            ("conflict", ["troops", "armed forces", "ceasefire"], ["invade", "invaded", "attack", "collapsed"]),
        ];
        foreach (var (topic, subjects, actions) in rules)
            if (ScoringText.FindTokenSequence(text, subjects) is not null
                && ScoringText.FindTokenSequence(text, actions) is not null) return topic;
        return null;
    }

    private bool HasCorroboration(
        NewsItem item,
        DateTimeOffset nowUtc,
        IReadOnlyList<NewsCorroboratingSource>? corroboratingSources,
        bool isPrimaryTierOne,
        List<string> factors)
    {
        var publications = new Dictionary<string, (string Label, DateTimeOffset PublishedUtc)>(StringComparer.Ordinal);

        var primaryLabel = SourceLabel(item.SourceId, item.SourceName);
        if (!isPrimaryTierOne)
        {
            factors.Add(
                $"Primary source '{primaryLabel}' is not tier-1, "
                + "so it does not count toward the two-source rule.");

            if (item.PublishedUtc is null)
            {
                factors.Add($"Primary source '{primaryLabel}' has no publication time; it cannot establish corroboration.");
            }
            else if (item.PublishedUtc > nowUtc)
            {
                factors.Add(
                    $"Primary source '{primaryLabel}' has a future publication time "
                    + $"({FormatInstant(item.PublishedUtc.Value)} relative to {FormatInstant(nowUtc)}); excluded.");
            }
        }
        else
        {
            AddPublication(
                publications,
                Organization(item.SourceId, item.SourceName),
                primaryLabel,
                item.PublishedUtc,
                nowUtc,
                "Primary source",
                factors);
        }

        foreach (var source in corroboratingSources ?? Array.Empty<NewsCorroboratingSource>())
        {
            var label = SourceLabel(source.SourceId, source.SourceName);
            var tier = _sourceTiers.ResolveTier(source.SourceId, source.SourceName);
            if (!string.Equals(tier, NewsSourceTier.TierOne, StringComparison.OrdinalIgnoreCase))
            {
                factors.Add(
                    $"Ignoring corroborating source '{label}' because it is not tier-1 "
                    + $"({(tier ?? "unclassified")}).");
                continue;
            }

            AddPublication(
                publications,
                Organization(source.SourceId, source.SourceName),
                label,
                source.PublishedUtc,
                nowUtc,
                "Corroborating tier-1 source",
                factors);
        }

        if (publications.Count < 2)
        {
            factors.Add(
                $"Only {publications.Count} independent tier-1 source(s) with a usable "
                + "publication time are available.");
            return false;
        }

        var independent = publications.Values.ToArray();
        TimeSpan? closest = null;
        for (var first = 0; first < independent.Length; first++)
        {
            for (var second = first + 1; second < independent.Length; second++)
            {
                var distance = (independent[first].PublishedUtc - independent[second].PublishedUtc).Duration();
                closest = closest is null || distance < closest ? distance : closest;
                if (distance <= _corroborationWindow)
                {
                    factors.Add(
                        $"{independent.Length} independent tier-1 sources published within "
                        + $"{FormatHours(distance)} ({FormatHours(_corroborationWindow)} window).");
                    return true;
                }
            }
        }

        factors.Add(
            $"Closest independent tier-1 publications are {FormatHours(closest ?? TimeSpan.Zero)} apart, "
            + $"outside the {FormatHours(_corroborationWindow)} window.");
        return false;
    }

    private static void AddPublication(
        Dictionary<string, (string Label, DateTimeOffset PublishedUtc)> publications,
        string? sourceId,
        string label,
        DateTimeOffset? publishedUtc,
        DateTimeOffset nowUtc,
        string role,
        List<string> factors)
    {
        if (publishedUtc is null)
        {
            factors.Add($"{role} '{label}' has no publication time; it cannot establish corroboration.");
            return;
        }

        if (publishedUtc > nowUtc)
        {
            factors.Add(
                $"{role} '{label}' has a future publication time ({FormatInstant(publishedUtc.Value)} "
                + $"relative to {FormatInstant(nowUtc)}); excluded.");
            return;
        }

        var key = ScoringText.Normalize(sourceId);
        if (key.Length == 0)
        {
            key = "(unknown source id)";
        }

        if (publications.ContainsKey(key))
        {
            factors.Add(
                $"Duplicate source id for '{label}' counted once; "
                + "corroboration requires independent sources.");
            return;
        }

        publications.Add(key, (label, publishedUtc.Value));
    }

    private static string SourceLabel(string? sourceId, string? sourceName)
    {
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            return sourceName;
        }

        return string.IsNullOrWhiteSpace(sourceId) ? "(unknown source)" : sourceId;
    }

    private static string Organization(string? id, string? name)
    {
        var value = ScoringText.Normalize(id) + " " + ScoringText.Normalize(name);
        if (ScoringText.ContainsTokenSequence(value, "bbc")) return "bbc";
        if (ScoringText.ContainsTokenSequence(value, "nyt") || ScoringText.ContainsTokenSequence(value, "new york times")) return "nyt";
        if (ScoringText.ContainsTokenSequence(value, "aljazeera") || ScoringText.ContainsTokenSequence(value, "al jazeera")) return "aljazeera";
        if (ScoringText.ContainsTokenSequence(value, "guardian")) return "guardian";
        return id ?? string.Empty;
    }

    private static string FormatInstant(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string FormatHours(TimeSpan value)
        => value.TotalHours.ToString("0.##", CultureInfo.InvariantCulture) + "h";
}
