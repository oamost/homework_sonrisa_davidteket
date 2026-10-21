using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorldAlerts.Api.Events;

namespace WorldAlerts.Api.Dedupe;

/// <summary>Provider fields whose change is treated as a material revision.</summary>
public enum EventRevisionField
{
    /// <summary>Headline/title text changed.</summary>
    Title,

    /// <summary>Summary/description text changed.</summary>
    Summary,

    /// <summary>Numeric or labelled severity changed.</summary>
    Severity,

    /// <summary>Alert level, alert score or tsunami flag changed.</summary>
    Alert,

    /// <summary>Observed market value or quote unit changed.</summary>
    Price,
}

/// <summary>
/// Extracts the small set of material provider fields used for revision
/// detection and fingerprints. Only fields actually supplied by the provider are
/// included; missing values are not invented. Provider timestamps alone do not
/// create a revision because a re-poll can refresh them without changing content.
/// </summary>
public static class EventMaterialFields
{
    /// <summary>Metadata key for the headline.</summary>
    public const string TitleKey = "title";

    /// <summary>Metadata key for the summary.</summary>
    public const string SummaryKey = "summary";

    /// <summary>Metadata key used by some feeds instead of <see cref="SummaryKey"/>.</summary>
    public const string DescriptionKey = "description";

    /// <summary>Metadata key for the observed market value.</summary>
    public const string ValueKey = "value";

    /// <summary>Metadata key for the market quote unit.</summary>
    public const string QuoteUnitKey = "quoteUnit";

    /// <summary>Metadata key for the disaster alert level.</summary>
    public const string AlertLevelKey = "alertLevel";

    /// <summary>Metadata key for the disaster alert score.</summary>
    public const string AlertScoreKey = "alertScore";

    /// <summary>Metadata key for the tsunami flag.</summary>
    public const string TsunamiKey = "tsunami";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>Extracts normalized material fields keyed by <see cref="EventRevisionField"/> name.</summary>
    public static IReadOnlyDictionary<string, string> Extract(EventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);

        Add(fields, EventRevisionField.Title, DedupeText.NormalizeTitle(Metadata(record, TitleKey)));
        Add(
            fields,
            EventRevisionField.Summary,
            DedupeText.Normalize(Metadata(record, SummaryKey) ?? Metadata(record, DescriptionKey)));
        Add(fields, EventRevisionField.Severity, SeverityValue(record));
        Add(fields, EventRevisionField.Alert, AlertValue(record));
        Add(fields, EventRevisionField.Price, PriceValue(record));

        return fields;
    }

    /// <summary>Serializes material fields for persistence. The output is invariant and ordered.</summary>
    public static string Serialize(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return JsonSerializer.Serialize(fields, SerializerOptions);
    }

    /// <summary>Parses persisted material fields; malformed payloads yield an empty set.</summary>
    public static IReadOnlyDictionary<string, string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null
                ? new SortedDictionary<string, string>(StringComparer.Ordinal)
                : new SortedDictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new SortedDictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// SHA-256 fingerprint over the ordered material fields. Equal fingerprints
    /// mean the provider-visible content is unchanged for revision purposes.
    /// </summary>
    public static string Fingerprint(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var builder = new StringBuilder();
        foreach (var field in fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(field.Key);
            builder.Append('\u001F');
            builder.Append(field.Value);
            builder.Append('\u001E');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Convenience overload that extracts and fingerprints one record.</summary>
    public static string Fingerprint(EventRecord record) => Fingerprint(Extract(record));

    /// <summary>Returns the material fields whose values differ between two snapshots.</summary>
    public static IReadOnlyList<EventRevisionField> Diff(
        IReadOnlyDictionary<string, string> previous,
        IReadOnlyDictionary<string, string> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var changed = new List<EventRevisionField>();
        var names = new SortedSet<string>(previous.Keys, StringComparer.Ordinal);
        names.UnionWith(current.Keys);

        foreach (var name in names)
        {
            previous.TryGetValue(name, out var oldValue);
            current.TryGetValue(name, out var newValue);
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal)
                && TryMap(name, out var field))
            {
                changed.Add(field);
            }
        }

        return changed;
    }

    /// <summary>Maps a persisted field name to its enum value.</summary>
    public static bool TryMap(string name, out EventRevisionField field)
    {
        switch (name)
        {
            case nameof(EventRevisionField.Title):
                field = EventRevisionField.Title;
                return true;
            case nameof(EventRevisionField.Summary):
                field = EventRevisionField.Summary;
                return true;
            case nameof(EventRevisionField.Severity):
                field = EventRevisionField.Severity;
                return true;
            case nameof(EventRevisionField.Alert):
                field = EventRevisionField.Alert;
                return true;
            case nameof(EventRevisionField.Price):
                field = EventRevisionField.Price;
                return true;
            default:
                field = default;
                return false;
        }
    }

    private static void Add(
        IDictionary<string, string> fields,
        EventRevisionField field,
        string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            fields[field.ToString()] = value;
        }
    }

    private static string? Metadata(EventRecord record, string key) =>
        record.Metadata is not null && record.Metadata.TryGetValue(key, out var value)
            ? value
            : null;

    private static string? SeverityValue(EventRecord record)
    {
        if (record.Severity is not { } severity)
        {
            return null;
        }

        var value = severity.ToString("R", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(record.SeverityUnit)
            ? value
            : value + " " + record.SeverityUnit.Trim().ToLowerInvariant();
    }

    private static string? AlertValue(EventRecord record)
    {
        var parts = new[]
        {
            Metadata(record, AlertLevelKey)?.Trim().ToLowerInvariant(),
            Metadata(record, AlertScoreKey)?.Trim(),
            Metadata(record, TsunamiKey)?.Trim().ToLowerInvariant(),
        }
        .Where(part => !string.IsNullOrEmpty(part))
        .ToArray();

        return parts.Length == 0 ? null : string.Join(' ', parts);
    }

    private static string? PriceValue(EventRecord record)
    {
        var value = Metadata(record, ValueKey)?.Trim();
        var unit = Metadata(record, QuoteUnitKey)?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value) && string.IsNullOrEmpty(unit))
        {
            return null;
        }

        return string.Join(' ', new[] { value, unit }.Where(part => !string.IsNullOrEmpty(part)));
    }
}
