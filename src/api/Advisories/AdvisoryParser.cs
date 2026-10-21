using System.Globalization;
using System.Text.Json;

namespace WorldAlerts.Api.Advisories;

/// <summary>
/// Parses the CISA Known Exploited Vulnerabilities JSON catalogue into the
/// advisory store's normalized rows. Source-native CVE ids are preserved.
/// </summary>
public static class AdvisoryParser
{
    public static AdvisoryParseResult Parse(
        string body,
        AdvisorySource source,
        DateTimeOffset fetchedUtc)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(body))
        {
            return new AdvisoryParseResult(Array.Empty<Advisory>(), 0);
        }

        using var document = JsonDocument.Parse(body);
        JsonElement vulnerabilities;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            vulnerabilities = document.RootElement;
        }
        else if (TryProperty(document.RootElement, "vulnerabilities", out var property)
            && property.ValueKind == JsonValueKind.Array)
        {
            vulnerabilities = property;
        }
        else
        {
            vulnerabilities = default;
        }

        if (vulnerabilities.ValueKind != JsonValueKind.Array)
        {
            return new AdvisoryParseResult(Array.Empty<Advisory>(), 1);
        }

        var advisories = new List<Advisory>();
        var skipped = 0;
        foreach (var item in vulnerabilities.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                skipped++;
                continue;
            }

            var cveId = StringValue(item, "cveID", "cveId", "cve");
            if (string.IsNullOrWhiteSpace(cveId))
            {
                skipped++;
                continue;
            }

            advisories.Add(new Advisory(
                SourceId: source.Id,
                CveId: cveId,
                Vendor: StringValue(item, "vendorProject", "vendor") ?? string.Empty,
                Product: StringValue(item, "product") ?? string.Empty,
                Name: StringValue(item, "vulnerabilityName", "name") ?? cveId,
                DateAdded: DateOnlyValue(item, "dateAdded", "date_added"),
                DueDate: DateOnlyValue(item, "dueDate", "due_date"),
                KnownRansomware: KnownRansomware(item),
                Provenance: source.Url,
                FetchedUtc: fetchedUtc.ToUniversalTime()));
        }

        return new AdvisoryParseResult(advisories, skipped);
    }

    private static bool KnownRansomware(JsonElement item)
    {
        if (TryProperty(item, "knownRansomware", out var flag))
        {
            if (flag.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (flag.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        var value = StringValue(item, "knownRansomwareCampaignUse", "known_ransomware");
        return string.Equals(value, "known", StringComparison.OrdinalIgnoreCase);
    }

    private static DateOnly? DateOnlyValue(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(
                    value.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? StringValue(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
            else if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetRawText();
            }
        }

        return null;
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
