using WorldAlerts.Api.Disasters;
using WorldAlerts.Api.News;
using WorldAlerts.Api.Scoring;

namespace WorldAlerts.Api.Selection;

/// <summary>Canonical subscription codes, derived from item metadata rather than feed identity alone.</summary>
public static class CanonicalEventTypes
{
    private static readonly Dictionary<string, string> NewsAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["conflict"] = "conflict", ["politics"] = "politics", ["diplomacy"] = "diplomacy",
        ["cyber"] = "tech.cyber", ["cybersecurity"] = "tech.cyber", ["tech.cyber"] = "tech.cyber",
        ["ai"] = "tech.ai", ["artificial intelligence"] = "tech.ai", ["tech.ai"] = "tech.ai",
        ["acquisitions"] = "tech.acquisitions", ["tech.acquisitions"] = "tech.acquisitions",
        ["critical infrastructure"] = "tech.critical-infrastructure", ["tech.critical-infrastructure"] = "tech.critical-infrastructure",
        ["environment"] = "environment.energy", ["energy"] = "environment.energy", ["environment.energy"] = "environment.energy",
        ["transformative-science"] = "science.transformative", ["science.transformative"] = "science.transformative",
    };

    /// <summary>
    /// Headline subject signals per approved news code. The subject vocabulary mirrors the
    /// approved consequential-topic rule so a story that the approved rule can promote keeps
    /// the matching subscription code. These are topic subjects only; they never make a story
    /// important, and a feed family alone never assigns a narrower code.
    /// </summary>
    private static readonly (string Code, string[] Words)[] NewsSubjectHints =
    [
        ("conflict", ["invasion", "airstrike", "missile", "ceasefire", "war", "troops", "armed forces"]),
        ("politics", ["election", "president", "parliament", "government"]),
        ("diplomacy", ["treaty", "sanctions", "summit", "diplomatic", "ceasefire"]),
        ("tech.cyber", ["ransomware", "cyberattack", "data breach", "hackers"]),
        ("tech.ai", ["artificial intelligence", "ai", "ai model", "ai system"]),
        ("tech.acquisitions", ["acquisition", "acquires", "merger"]),
        ("tech.critical-infrastructure", ["power grid", "water supply", "telecom outage", "telecom network", "hospital network"]),
        ("environment.energy", ["climate", "renewable energy", "oil spill", "power plant", "emissions", "energy grid"]),
        ("science.transformative", ["clinical trial", "fusion reactor", "scientific breakthrough"]),
    ];

    public static string[] News(NewsItem item)
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world-news" };
        foreach (var category in item.Categories)
            if (NewsAliases.TryGetValue(category.Trim(), out var code)) types.Add(code);
        // The configured feed family is topical context only. Require an item-specific
        // headline/category signal before assigning a narrower subscription type.
        var headline = ScoringText.Normalize(item.Title);
        foreach (var (code, words) in NewsSubjectHints)
            if (words.Any(word => ScoringText.ContainsTokenSequence(headline, word.Trim()))) types.Add(code);
        return types.OrderBy(code => code, StringComparer.Ordinal).ToArray();
    }

    public static string[] Disaster(DisasterEvent item)
    {
        var type = ScoringText.Normalize(item.EventType);
        var title = ScoringText.Normalize(item.Title);
        var hazard = type switch
        {
            "earthquake" or "eq" => "disaster.earthquake",
            "flood" or "fl" or "floods" => "disaster.flood",
            "cyclone" or "tc" or "tropical cyclone" or "hurricane" or "typhoon" => "disaster.cyclone",
            "severe storms" when HasCycloneEvidence(title) => "disaster.cyclone",
            "storm" when HasCycloneEvidence(title) => "disaster.cyclone",
            "wildfire" or "wildfires" or "fire" or "wf" => "disaster.wildfire",
            "volcano" or "volcanoes" or "vo" => "disaster.volcano",
            "drought" or "dr" => "disaster.drought",
            "tsunami" or "ts" => "disaster.tsunami",
            "landslide" or "landslides" or "ls" => "disaster.landslide",
            "mass movement" => "disaster.landslide",
            "epidemic" or "epidemics" or "disease outbreak" or "disease" => "disaster.epidemic",
            "humanitarian" or "complex emergency" or "population movement" => "health.humanitarian",
            _ => HazardFromProviderLabel(type, title),
        };
        return new[] { hazard, item.Tsunami is true ? "disaster.tsunami" : null }
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Maps provider hazard labels that carry an approved code but are not named verbatim above.
    /// A specific hazard label is used as-is; a generic label (for example <c>other</c> or
    /// <c>storm</c>) only maps when the event title names the hazard. No code is invented for a
    /// hazard the approved taxonomy does not cover.
    /// </summary>
    private static string? HazardFromProviderLabel(string type, string title)
    {
        if (Has(type, "flood")) return "disaster.flood";
        if (Has(type, "volcan")) return "disaster.volcano";
        if (Has(type, "landslid") || Has(type, "mass movement")) return "disaster.landslide";
        if (Has(type, "cyclon") || Has(type, "hurrican") || Has(type, "typhoon")) return "disaster.cyclone";
        if (Has(type, "epidem") || Has(type, "outbreak") || Has(type, "pandemic")) return "disaster.epidemic";
        if (Has(type, "population movement") || Has(type, "mass removal")) return "health.humanitarian";
        if (Has(title, "flood")) return "disaster.flood";
        if (Has(title, "volcan")) return "disaster.volcano";
        if (Has(title, "landslid")) return "disaster.landslide";
        if (Has(title, "cyclon") || Has(title, "hurrican") || Has(title, "typhoon")) return "disaster.cyclone";
        if (Has(title, "epidem") || Has(title, "outbreak")) return "disaster.epidemic";
        if (Has(title, "population movement") || Has(title, "mass removal")) return "health.humanitarian";
        return null;
    }

    private static bool HasCycloneEvidence(string title) =>
        Has(title, "cyclon") || Has(title, "hurrican") || Has(title, "typhoon");

    private static bool Has(string value, string fragment) =>
        value.Contains(fragment, StringComparison.Ordinal);
}
