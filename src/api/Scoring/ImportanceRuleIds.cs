namespace WorldAlerts.Api.Scoring;

/// <summary>Stable identifiers for the approved news importance rules.</summary>
public static class NewsImportanceRuleIds
{
    /// <summary>A tier-1 source reported an approved urgency keyword in the headline.</summary>
    public const string TierOneUrgencyKeyword = "news.tier-1-urgency-keyword";

    /// <summary>Two independent tier-1 sources carried the story within six hours.</summary>
    public const string TierOneCorroboration = "news.tier-1-corroboration";

    /// <summary>No approved news rule matched.</summary>
    public const string NoApprovedTrigger = "news.no-approved-trigger";
}

/// <summary>Stable identifiers for the approved market importance rules.</summary>
public static class MarketImportanceRuleIds
{
    /// <summary>The absolute move met the approved family threshold.</summary>
    public const string MoveThreshold = "market.move-threshold";

    /// <summary>The absolute move was below the approved family threshold.</summary>
    public const string BelowThreshold = "market.below-threshold";

    /// <summary>The observation was not fresher than twice its interval.</summary>
    public const string StaleObservation = "market.stale-observation";

    /// <summary>No baseline value was supplied.</summary>
    public const string MissingBaseline = "market.missing-baseline";

    /// <summary>The baseline value was zero, so a percentage move is undefined.</summary>
    public const string InvalidBaseline = "market.invalid-baseline";

    /// <summary>The observation interval was not positive.</summary>
    public const string InvalidInterval = "market.invalid-interval";
}

/// <summary>Stable identifiers for the approved disaster importance rules.</summary>
public static class DisasterImportanceRuleIds
{
    /// <summary>A GDACS Orange or Red alert was reported.</summary>
    public const string GdacsOrangeRed = "disaster.gdacs-orange-red";

    /// <summary>A Green earthquake met the approved magnitude fixture.</summary>
    public const string EarthquakeMagnitude = "disaster.earthquake-magnitude";

    /// <summary>A tsunami flag was set.</summary>
    public const string TsunamiFlag = "disaster.tsunami-flag";

    /// <summary>An IFRC event met the approved severity fixture.</summary>
    public const string IfrcSeverity = "disaster.ifrc-severity";

    /// <summary>No approved disaster rule matched.</summary>
    public const string NoApprovedTrigger = "disaster.no-approved-trigger";
}
