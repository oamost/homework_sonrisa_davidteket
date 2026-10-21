namespace WorldAlerts.Api.Scoring;

/// <summary>Tier names used by the approved news rule.</summary>
public static class NewsSourceTier
{
    /// <summary>A global source whose single report can trigger the keyword rule.</summary>
    public const string TierOne = "tier-1";

    /// <summary>A specialist or trade source that does not satisfy the tier-1 requirement.</summary>
    public const string Specialist = "specialist";
}

/// <summary>One configurable source-to-tier mapping.</summary>
/// <param name="SourceId">Configured source id, for example <c>bbc-world</c>.</param>
/// <param name="SourceName">Configured display name, for example <c>BBC World</c>.</param>
/// <param name="Tier">Tier name; <see cref="NewsSourceTier.TierOne"/> is the approved trigger tier.</param>
public sealed record NewsSourceTierDefinition(string SourceId, string SourceName, string Tier);

/// <summary>
/// Configurable source tiers for the news rule. The defaults are the approved
/// acceptance fixtures: BBC, NYT, Al Jazeera and The Guardian are tier-1;
/// Ars Technica and Nature are specialist. These defaults are examples, not a
/// universal claim about editorial quality.
/// </summary>
public sealed class NewsSourceTiers
{
    private readonly IReadOnlyList<NewsSourceTierDefinition> _definitions;

    /// <summary>Creates a tier map from explicit definitions.</summary>
    public NewsSourceTiers(IEnumerable<NewsSourceTierDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions.ToArray();
    }

    /// <summary>The approved default tier map.</summary>
    public static NewsSourceTiers Default { get; } = CreateDefault();

    /// <summary>Creates the approved default tier map.</summary>
    public static NewsSourceTiers CreateDefault()
    {
        return new NewsSourceTiers(new[]
        {
            new NewsSourceTierDefinition("bbc-world", "BBC World", NewsSourceTier.TierOne),
            new NewsSourceTierDefinition("bbc", "BBC", NewsSourceTier.TierOne),
            new NewsSourceTierDefinition("nyt-world", "NYT World", NewsSourceTier.TierOne),
            new NewsSourceTierDefinition("nyt", "NYT", NewsSourceTier.TierOne),
            new NewsSourceTierDefinition("aljazeera-all", "Al Jazeera English", NewsSourceTier.TierOne),
            new NewsSourceTierDefinition("aljazeera", "Al Jazeera", NewsSourceTier.TierOne),
            new NewsSourceTierDefinition("guardian-world", "The Guardian World", NewsSourceTier.TierOne),
            new NewsSourceTierDefinition("guardian", "Guardian", NewsSourceTier.TierOne),
            new NewsSourceTierDefinition("ars-technica", "Ars Technica", NewsSourceTier.Specialist),
            new NewsSourceTierDefinition("nature", "Nature", NewsSourceTier.Specialist),
        });
    }

    /// <summary>Resolves a source tier by id or display name, or null when unclassified.</summary>
    public string? ResolveTier(string? sourceId, string? sourceName)
    {
        var normalizedId = ScoringText.Normalize(sourceId);
        var normalizedName = ScoringText.Normalize(sourceName);

        foreach (var definition in _definitions)
        {
            if (MatchesExactly(normalizedId, normalizedName, definition))
            {
                return definition.Tier;
            }
        }

        foreach (var definition in _definitions)
        {
            if (MatchesTokenSequence(normalizedId, normalizedName, definition))
            {
                return definition.Tier;
            }
        }

        return null;
    }

    /// <summary>Returns true only when the source resolves to the approved tier-1 name.</summary>
    public bool IsTierOne(string? sourceId, string? sourceName)
        => string.Equals(ResolveTier(sourceId, sourceName), NewsSourceTier.TierOne, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesExactly(
        string normalizedId,
        string normalizedName,
        NewsSourceTierDefinition definition)
    {
        var definitionId = ScoringText.Normalize(definition.SourceId);
        var definitionName = ScoringText.Normalize(definition.SourceName);
        return (normalizedId.Length > 0 && normalizedId == definitionId)
            || (normalizedName.Length > 0 && normalizedName == definitionName);
    }

    private static bool MatchesTokenSequence(
        string normalizedId,
        string normalizedName,
        NewsSourceTierDefinition definition)
    {
        var definitionId = ScoringText.Normalize(definition.SourceId);
        var definitionName = ScoringText.Normalize(definition.SourceName);
        return ScoringText.ContainsTokenSequence(normalizedId, definitionId)
            || ScoringText.ContainsTokenSequence(normalizedId, definitionName)
            || ScoringText.ContainsTokenSequence(normalizedName, definitionId)
            || ScoringText.ContainsTokenSequence(normalizedName, definitionName);
    }
}
