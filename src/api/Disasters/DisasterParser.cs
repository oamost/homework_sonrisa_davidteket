using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace WorldAlerts.Api.Disasters;

/// <summary>
/// Parses the supported keyless disaster payload shapes into normalized events.
/// Values a provider does not supply (country, coordinates, severity, times)
/// remain absent. An empty body is a valid zero-event result, never a crash.
/// </summary>
public static class DisasterParser
{
    public const string GdacsRss = "gdacs-rss";
    public const string Eonet = "eonet";
    public const string Usgs = "usgs";
    public const string Ifrc = "ifrc";
    public const string Nws = "nws";
    public const string DwdWarnings = "dwd-warnings";
    public const string CopernicusEms = "copernicus-ems";
    public const string Emsc = "emsc";

    // DWD's warnapp payload uses these event labels for heat and cold. The live
    // probe on 2026-10-21 returned only BÖEN/WINDBÖEN (winter wind warnings), so
    // these heat/frost labels are DWD's documented names rather than values seen
    // in that particular payload.
    private static readonly string[] DefaultDwdEvents =
    {
        "HITZE",
        "EXTREME HITZE",
        "FROST",
        "STRENGE FROST",
    };

    public static DisasterParseResult Parse(
        string body,
        DisasterSource source,
        DateTimeOffset fetchedUtc)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(body))
        {
            return new DisasterParseResult(Array.Empty<DisasterEvent>(), 0);
        }

        return source.Kind switch
        {
            GdacsRss => ParseGdacs(body, source, fetchedUtc),
            Eonet => ParseEonet(body, source, fetchedUtc),
            Usgs => ParseUsgs(body, source, fetchedUtc),
            Ifrc => ParseIfrc(body, source, fetchedUtc),
            Nws => ParseNws(body, source, fetchedUtc),
            DwdWarnings => ParseDwd(body, source, fetchedUtc),
            CopernicusEms => ParseEms(body, source, fetchedUtc),
            Emsc => ParseEmsc(body, source, fetchedUtc),
            _ => throw new InvalidOperationException($"Unsupported disaster source kind '{source.Kind}'."),
        };
    }

    /// <summary>
    /// Parses several responses for one source and merges them by native id. This
    /// is used for multi-category EONET, multi-event NWS and paged EMS polls.
    /// Empty bodies are ignored; a source response with an empty array is a valid
    /// zero-event result.
    /// </summary>
    public static DisasterParseResult ParseMany(
        IEnumerable<string> bodies,
        DisasterSource source,
        DateTimeOffset fetchedUtc)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(source);

        var events = new List<DisasterEvent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;

        foreach (var body in bodies)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var parsed = Parse(body, source, fetchedUtc);
            skipped += parsed.Skipped;
            foreach (var item in parsed.Items)
            {
                if (seen.Add(item.NativeId))
                {
                    events.Add(item);
                }
            }
        }

        return new DisasterParseResult(events, skipped);
    }

    private static DisasterParseResult ParseGdacs(
        string body,
        DisasterSource source,
        DateTimeOffset fetchedUtc)
    {
        var document = XDocument.Parse(body, LoadOptions.None);
        var events = new List<DisasterEvent>();
        var skipped = 0;

        foreach (var item in document.Descendants().Where(element => IsNamed(element, "item")))
        {
            var title = ChildValue(item, "title");
            var nativeId = ChildValue(item, "eventid")
                ?? ChildValue(item, "guid")
                ?? ChildValue(item, "link");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(nativeId))
            {
                skipped++;
                continue;
            }

            var severityElement = Child(item, "severity");
            var severity = severityElement is null ? null : AttributeOrChildDouble(severityElement, "value");
            var severityUnit = severityElement is null ? null : AttributeOrChildValue(severityElement, "unit");

            var (latitude, longitude) = Coordinates(item);

            events.Add(new DisasterEvent(
                SourceId: source.Id,
                NativeId: nativeId,
                EventType: NormalizeGdacsType(ChildValue(item, "eventtype")),
                Title: title,
                EventTimeUtc: ParseDate(ChildValue(item, "fromdate"))
                    ?? ParseDate(ChildValue(item, "todate")),
                PublishedUtc: ParseDate(ChildValue(item, "pubDate")),
                UpdatedUtc: ParseDate(ChildValue(item, "datemodified"))
                    ?? ParseDate(ChildValue(item, "dateadded")),
                Latitude: latitude,
                Longitude: longitude,
                Country: ChildValue(item, "country"),
                Iso3: ChildValue(item, "iso3"),
                Severity: severity,
                SeverityUnit: severityUnit,
                AlertLevel: ChildValue(item, "alertlevel"),
                AlertScore: ChildDouble(item, "alertscore"),
                Tsunami: null,
                Significance: null,
                Provenance: source.Url,
                FetchedUtc: fetchedUtc.ToUniversalTime()));
        }

        return new DisasterParseResult(events, skipped);
    }

    private static DisasterParseResult ParseEonet(
        string body,
        DisasterSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        if (!TryProperty(document.RootElement, "events", out var eventsElement)
            || eventsElement.ValueKind != JsonValueKind.Array)
        {
            return new DisasterParseResult(Array.Empty<DisasterEvent>(), 1);
        }

        var events = new List<DisasterEvent>();
        var skipped = 0;
        foreach (var item in eventsElement.EnumerateArray())
        {
            var nativeId = StringValue(item, "id");
            var title = StringValue(item, "title");
            if (string.IsNullOrWhiteSpace(nativeId) || string.IsNullOrWhiteSpace(title))
            {
                skipped++;
                continue;
            }

            var type = NestedString(item, "categories", "id")
                ?? NestedString(item, "categories", "title")
                ?? "event";

            var geometry = LatestGeometry(item);
            var eventTime = geometry is null ? null : DateValue(geometry.Value, "date");

            double? severity = null;
            string? severityUnit = null;
            if (geometry is not null)
            {
                severity = DoubleValue(geometry.Value, "magnitudeValue");
                severityUnit = StringValue(geometry.Value, "magnitudeUnit");
            }

            if (severity is null
                && TryProperty(item, "geometry", out var allGeometry)
                && allGeometry.ValueKind == JsonValueKind.Array)
            {
                foreach (var candidate in allGeometry.EnumerateArray().Reverse())
                {
                    if (DoubleValue(candidate, "magnitudeValue") is not { } historical)
                    {
                        continue;
                    }

                    severity = historical;
                    severityUnit = StringValue(candidate, "magnitudeUnit");
                    break;
                }
            }

            var (latitude, longitude) = geometry is null
                ? ((double?)null, (double?)null)
                : PointCoordinates(geometry.Value);

            events.Add(new DisasterEvent(
                SourceId: source.Id,
                NativeId: nativeId,
                EventType: type,
                Title: title,
                EventTimeUtc: eventTime,
                PublishedUtc: null,
                UpdatedUtc: null,
                Latitude: latitude,
                Longitude: longitude,
                Country: null,
                Iso3: null,
                Severity: severity,
                SeverityUnit: severityUnit,
                AlertLevel: null,
                AlertScore: null,
                Tsunami: null,
                Significance: null,
                Provenance: source.Url,
                FetchedUtc: fetchedUtc.ToUniversalTime()));
        }

        return new DisasterParseResult(events, skipped);
    }

    private static DisasterParseResult ParseUsgs(
        string body,
        DisasterSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        if (!TryProperty(document.RootElement, "features", out var features)
            || features.ValueKind != JsonValueKind.Array)
        {
            return new DisasterParseResult(Array.Empty<DisasterEvent>(), 1);
        }

        var events = new List<DisasterEvent>();
        var skipped = 0;
        foreach (var feature in features.EnumerateArray())
        {
            var nativeId = StringValue(feature, "id");
            if (string.IsNullOrWhiteSpace(nativeId)
                || !TryProperty(feature, "properties", out var properties))
            {
                skipped++;
                continue;
            }

            var title = StringValue(properties, "title")
                ?? StringValue(properties, "place")
                ?? nativeId;
            var longitude = (double?)null;
            var latitude = (double?)null;
            if (TryProperty(feature, "geometry", out var geometry)
                && TryProperty(geometry, "coordinates", out var coordinates)
                && coordinates.ValueKind == JsonValueKind.Array
                && coordinates.GetArrayLength() >= 2)
            {
                longitude = Number(coordinates[0]);
                latitude = Number(coordinates[1]);
            }

            var tsunami = LongValue(properties, "tsunami") is { } flag ? flag != 0 : (bool?)null;

            events.Add(new DisasterEvent(
                SourceId: source.Id,
                NativeId: nativeId,
                EventType: StringValue(properties, "type") ?? "earthquake",
                Title: title,
                EventTimeUtc: UnixMilliseconds(properties, "time"),
                PublishedUtc: null,
                UpdatedUtc: UnixMilliseconds(properties, "updated"),
                Latitude: latitude,
                Longitude: longitude,
                Country: null,
                Iso3: null,
                Severity: DoubleValue(properties, "mag"),
                SeverityUnit: StringValue(properties, "magType") ?? "magnitude",
                AlertLevel: StringValue(properties, "alert"),
                AlertScore: null,
                Tsunami: tsunami,
                Significance: LongValue(properties, "sig"),
                Provenance: StringValue(properties, "url") ?? source.Url,
                FetchedUtc: fetchedUtc.ToUniversalTime()));
        }

        return new DisasterParseResult(events, skipped);
    }

    private static DisasterParseResult ParseIfrc(
        string body,
        DisasterSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        var events = new List<DisasterEvent>();
        var skipped = 0;
        var apiBase = source.Url.TrimEnd('/');

        foreach (var item in RootItems(document.RootElement, "results", "value", "items", "data"))
        {
            var nativeId = StringValue(item, "id");
            var title = StringValue(item, "name");
            if (string.IsNullOrWhiteSpace(nativeId) || string.IsNullOrWhiteSpace(title))
            {
                skipped++;
                continue;
            }

            var type = NestedString(item, "dtype", "name")
                ?? StringValue(item, "disaster_type")
                ?? "event";

            var country = (string?)null;
            var iso3 = (string?)null;
            if (TryProperty(item, "countries", out var countries)
                && countries.ValueKind == JsonValueKind.Array
                && countries.GetArrayLength() > 0
                && countries[0].ValueKind == JsonValueKind.Object)
            {
                country = StringValue(countries[0], "name");
                iso3 = StringValue(countries[0], "iso3");
            }

            var severity = (double?)null;
            var alertLevel = (string?)null;
            if (TryProperty(item, "ifrc_severity_level", out var level))
            {
                if (level.ValueKind == JsonValueKind.Number && level.TryGetDouble(out var numeric))
                {
                    severity = numeric;
                }
                else if (level.ValueKind == JsonValueKind.String)
                {
                    alertLevel = level.GetString();
                }
            }

            alertLevel ??= StringValue(item, "severity_level_display", "severity_level");
            var provenance = $"{apiBase}/{nativeId}/";

            events.Add(new DisasterEvent(
                SourceId: source.Id,
                NativeId: nativeId,
                EventType: type,
                Title: title,
                EventTimeUtc: DateValue(item, "start_date", "startDate"),
                PublishedUtc: DateValue(item, "created_at", "createdAt"),
                UpdatedUtc: DateValue(item, "modified_at", "modifiedAt", "updated_at"),
                Latitude: DoubleValue(item, "latitude"),
                Longitude: DoubleValue(item, "longitude"),
                Country: country,
                Iso3: iso3,
                Severity: severity,
                SeverityUnit: severity is null ? null : "IFRC severity level",
                AlertLevel: alertLevel,
                AlertScore: null,
                Tsunami: null,
                Significance: null,
                Provenance: provenance,
                FetchedUtc: fetchedUtc.ToUniversalTime()));
        }

        return new DisasterParseResult(events, skipped);
    }

    private static DisasterParseResult ParseNws(
        string body,
        DisasterSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        if (!TryProperty(document.RootElement, "features", out var features)
            || features.ValueKind != JsonValueKind.Array)
        {
            return new DisasterParseResult(Array.Empty<DisasterEvent>(), 1);
        }

        var events = new List<DisasterEvent>();
        var skipped = 0;
        foreach (var feature in features.EnumerateArray())
        {
            if (!TryProperty(feature, "properties", out var properties)
                || properties.ValueKind != JsonValueKind.Object)
            {
                skipped++;
                continue;
            }

            var nativeId = StringValue(properties, "id")
                ?? StringValue(feature, "id");
            var eventName = StringValue(properties, "event");
            var title = StringValue(properties, "headline")
                ?? eventName
                ?? StringValue(properties, "areaDesc")
                ?? nativeId;
            if (string.IsNullOrWhiteSpace(nativeId) || string.IsNullOrWhiteSpace(title))
            {
                skipped++;
                continue;
            }

            var severity = StringValue(properties, "severity");
            var onset = DateValue(properties, "onset");
            var effective = DateValue(properties, "effective");
            var expires = DateValue(properties, "expires");
            var (latitude, longitude) = GeometryCentroid(feature);

            events.Add(new DisasterEvent(
                SourceId: source.Id,
                NativeId: nativeId,
                EventType: eventName ?? "weather-alert",
                Title: title,
                EventTimeUtc: onset ?? effective,
                PublishedUtc: DateValue(properties, "sent"),
                UpdatedUtc: null,
                Latitude: latitude,
                Longitude: longitude,
                Country: "United States",
                Iso3: "USA",
                Severity: null,
                SeverityUnit: severity is null ? null : "NWS severity",
                AlertLevel: severity,
                AlertScore: null,
                Tsunami: null,
                Significance: null,
                Provenance: WithSemantics(source),
                FetchedUtc: fetchedUtc.ToUniversalTime(),
                Certainty: StringValue(properties, "certainty"),
                EffectiveUtc: effective,
                ExpiresUtc: expires));
        }

        return new DisasterParseResult(events, skipped);
    }

    private static DisasterParseResult ParseDwd(
        string body,
        DisasterSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = ParseDwdDocument(body);
        var root = document.RootElement;
        if (!TryProperty(root, "warnings", out var warnings)
            || warnings.ValueKind == JsonValueKind.Null)
        {
            return new DisasterParseResult(Array.Empty<DisasterEvent>(), 0);
        }

        if (warnings.ValueKind != JsonValueKind.Object)
        {
            return new DisasterParseResult(Array.Empty<DisasterEvent>(), 1);
        }

        IEnumerable<string> configuredEvents = source.Events is { Count: > 0 }
            ? source.Events
            : DefaultDwdEvents;
        var allowedEvents = new HashSet<string>(
            configuredEvents
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var publishedUtc = UnixMilliseconds(root, "time");
        var events = new List<DisasterEvent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;

        foreach (var region in warnings.EnumerateObject())
        {
            if (region.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (region.Value.ValueKind != JsonValueKind.Array)
            {
                skipped++;
                continue;
            }

            foreach (var warning in region.Value.EnumerateArray())
            {
                if (warning.ValueKind != JsonValueKind.Object)
                {
                    skipped++;
                    continue;
                }

                var eventName = StringValue(warning, "event");
                if (string.IsNullOrWhiteSpace(eventName)
                    || !allowedEvents.Contains(eventName.Trim()))
                {
                    continue;
                }

                var start = UnixMilliseconds(warning, "start");
                var end = UnixMilliseconds(warning, "end");
                var startKey = start?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? "na";
                var endKey = end?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? "na";
                var regionName = StringValue(warning, "regionName");
                var regionKey = string.IsNullOrWhiteSpace(regionName)
                    ? region.Name
                    : regionName.Trim();
                var nativeId = $"dwd:{regionKey}:{eventName.Trim()}:{startKey}:{endKey}";
                if (!seen.Add(nativeId))
                {
                    continue;
                }

                var level = DoubleValue(warning, "level");
                var headline = StringValue(warning, "headline") ?? eventName;
                var title = string.IsNullOrWhiteSpace(regionName)
                    ? headline
                    : $"{headline} - {regionName}";

                events.Add(new DisasterEvent(
                    SourceId: source.Id,
                    NativeId: nativeId,
                    EventType: NormalizeSourceType(eventName),
                    Title: title,
                    EventTimeUtc: start,
                    PublishedUtc: publishedUtc,
                    UpdatedUtc: null,
                    Latitude: null,
                    Longitude: null,
                    Country: "Germany",
                    Iso3: "DEU",
                    Severity: level,
                    SeverityUnit: level is null ? null : "DWD warning level",
                    AlertLevel: StringValue(warning, "level"),
                    AlertScore: null,
                    Tsunami: null,
                    Significance: null,
                    Provenance: WithSemantics(source),
                    FetchedUtc: fetchedUtc.ToUniversalTime(),
                    EffectiveUtc: start,
                    ExpiresUtc: end));
            }
        }

        return new DisasterParseResult(events, skipped);
    }

    private static JsonDocument ParseDwdDocument(string body)
    {
        var text = body.Trim().TrimStart('\uFEFF');
        if (text.StartsWith('{'))
        {
            return JsonDocument.Parse(text);
        }

        var open = text.IndexOf('{');
        var close = text.LastIndexOf('}');
        if (open < 0 || close <= open)
        {
            throw new JsonException("DWD warnings payload is neither JSON nor JSONP.");
        }

        return JsonDocument.Parse(text[open..(close + 1)]);
    }

    private static DisasterParseResult ParseEms(
        string body,
        DisasterSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array
            && !HasArrayProperty(document.RootElement, "results", "activations", "data", "items", "value"))
        {
            return new DisasterParseResult(Array.Empty<DisasterEvent>(), 1);
        }

        var events = new List<DisasterEvent>();
        var skipped = 0;
        foreach (var item in RootItems(document.RootElement, "results", "activations", "data", "items", "value"))
        {
            var title = StringValue(item, "name", "title", "activationName");
            var nativeId = StringValue(item, "id", "activationId", "activation_id", "code", "name")
                ?? title;
            if (string.IsNullOrWhiteSpace(nativeId) || string.IsNullOrWhiteSpace(title))
            {
                skipped++;
                continue;
            }

            var category = CategoryValue(item);
            var (country, iso3) = FirstCountry(item);
            var (latitude, longitude) = WktPoint(CentroidText(item));

            events.Add(new DisasterEvent(
                SourceId: source.Id,
                NativeId: nativeId,
                EventType: NormalizeSourceType(category),
                Title: title,
                EventTimeUtc: DateValue(item, "eventTime", "event_time", "eventDate", "event_date"),
                PublishedUtc: DateValue(item, "activationTime", "activation_time"),
                UpdatedUtc: DateValue(item, "lastUpdate", "last_update"),
                Latitude: latitude,
                Longitude: longitude,
                Country: country,
                Iso3: iso3,
                Severity: null,
                SeverityUnit: null,
                AlertLevel: null,
                AlertScore: null,
                Tsunami: null,
                Significance: null,
                Provenance: WithSemantics(source),
                FetchedUtc: fetchedUtc.ToUniversalTime()));
        }

        return new DisasterParseResult(events, skipped);
    }

    private static DisasterParseResult ParseEmsc(
        string body,
        DisasterSource source,
        DateTimeOffset fetchedUtc)
    {
        using var document = JsonDocument.Parse(body);
        if (!TryProperty(document.RootElement, "features", out var features)
            || features.ValueKind != JsonValueKind.Array)
        {
            return new DisasterParseResult(Array.Empty<DisasterEvent>(), 1);
        }

        var events = new List<DisasterEvent>();
        var skipped = 0;
        foreach (var feature in features.EnumerateArray())
        {
            if (!TryProperty(feature, "properties", out var properties)
                || properties.ValueKind != JsonValueKind.Object)
            {
                skipped++;
                continue;
            }

            var nativeId = StringValue(feature, "id")
                ?? StringValue(properties, "eventID", "eventId", "id");
            if (string.IsNullOrWhiteSpace(nativeId))
            {
                skipped++;
                continue;
            }

            var region = StringValue(properties, "flynn_region", "region", "place")
                ?? "Earthquake";
            var magnitude = DoubleValue(properties, "mag", "magnitude");
            var magnitudeUnit = StringValue(properties, "magtype", "magType") ?? "magnitude";
            var (latitude, longitude, geometryDepth) = EmscGeometry(feature);
            var depth = DoubleValue(properties, "depth") ?? geometryDepth;
            var title = magnitude is null
                ? region
                : $"M {magnitude.Value.ToString("0.0", CultureInfo.InvariantCulture)} - {region}";

            events.Add(new DisasterEvent(
                SourceId: source.Id,
                NativeId: nativeId,
                EventType: "earthquake",
                Title: title,
                EventTimeUtc: DateValue(properties, "time"),
                PublishedUtc: null,
                UpdatedUtc: null,
                Latitude: latitude ?? DoubleValue(properties, "lat", "latitude"),
                Longitude: longitude ?? DoubleValue(properties, "lon", "longitude"),
                Country: null,
                Iso3: null,
                Severity: magnitude,
                SeverityUnit: magnitudeUnit,
                AlertLevel: null,
                AlertScore: null,
                Tsunami: null,
                Significance: null,
                Provenance: WithSemantics(source),
                FetchedUtc: fetchedUtc.ToUniversalTime(),
                DepthKm: depth));
        }

        return new DisasterParseResult(events, skipped);
    }

    private static string? CategoryValue(JsonElement item)
    {
        if (!TryProperty(item, "category", out var category))
        {
            return null;
        }

        if (category.ValueKind == JsonValueKind.String)
        {
            return StringValue(item, "category");
        }

        return category.ValueKind == JsonValueKind.Object
            ? StringValue(category, "name", "title", "id")
            : null;
    }

    private static (string? Country, string? Iso3) FirstCountry(JsonElement item)
    {
        if (!TryProperty(item, "countries", out var countries))
        {
            var country = StringValue(item, "country");
            return (country, null);
        }

        if (countries.ValueKind == JsonValueKind.String)
        {
            return (StringValue(item, "countries"), null);
        }

        if (countries.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        foreach (var entry in countries.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                return (entry.GetString(), null);
            }

            if (entry.ValueKind == JsonValueKind.Object)
            {
                return (
                    StringValue(entry, "name", "country"),
                    StringValue(entry, "iso3", "iso3Code", "code"));
            }
        }

        return (null, null);
    }

    private static string? CentroidText(JsonElement item)
    {
        var text = StringValue(item, "centroid", "centroidWkt", "centroid_wkt");
        if (text is not null)
        {
            return text;
        }

        if (TryProperty(item, "centroid", out var centroid)
            && centroid.ValueKind == JsonValueKind.Object)
        {
            return StringValue(centroid, "wkt", "value");
        }

        return null;
    }

    private static (double? Latitude, double? Longitude) WktPoint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return (null, null);
        }

        var parts = text[(open + 1)..close]
            .Replace(',', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return (null, null);
        }

        return (ParseDouble(parts[1]), ParseDouble(parts[0]));
    }

    private static (double? Latitude, double? Longitude) GeometryCentroid(JsonElement feature)
    {
        if (!TryProperty(feature, "geometry", out var geometry)
            || geometry.ValueKind != JsonValueKind.Object
            || !TryProperty(geometry, "coordinates", out var coordinates))
        {
            return (null, null);
        }

        var points = new List<(double Longitude, double Latitude)>();
        CollectCoordinatePairs(coordinates, points);
        if (points.Count == 0)
        {
            return (null, null);
        }

        return (
            points.Average(point => point.Latitude),
            points.Average(point => point.Longitude));
    }

    private static void CollectCoordinatePairs(
        JsonElement element,
        List<(double Longitude, double Latitude)> points)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        if (element.GetArrayLength() >= 2
            && element[0].ValueKind == JsonValueKind.Number
            && element[1].ValueKind == JsonValueKind.Number
            && element[0].TryGetDouble(out var longitude)
            && element[1].TryGetDouble(out var latitude))
        {
            points.Add((longitude, latitude));
            return;
        }

        foreach (var child in element.EnumerateArray())
        {
            CollectCoordinatePairs(child, points);
        }
    }

    private static (double? Latitude, double? Longitude, double? DepthKm) EmscGeometry(JsonElement feature)
    {
        if (!TryProperty(feature, "geometry", out var geometry)
            || geometry.ValueKind != JsonValueKind.Object
            || !TryProperty(geometry, "coordinates", out var coordinates)
            || coordinates.ValueKind != JsonValueKind.Array
            || coordinates.GetArrayLength() < 2)
        {
            return (null, null, null);
        }

        var longitude = Number(coordinates[0]);
        var latitude = Number(coordinates[1]);
        var depth = coordinates.GetArrayLength() >= 3 ? Number(coordinates[2]) : null;
        return (latitude, longitude, depth);
    }

    private static string NormalizeSourceType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "event";
        }

        var normalized = value.Trim().ToLowerInvariant().Replace('_', ' ');
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "event" : string.Join('-', parts);
    }

    private static string WithSemantics(DisasterSource source) =>
        string.IsNullOrWhiteSpace(source.Semantics)
            ? source.Url
            : $"{source.Url} ({source.Semantics})";

    private static bool HasArrayProperty(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryProperty(root, name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<JsonElement> RootItems(JsonElement root, params string[] propertyNames)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray();
        }

        foreach (var name in propertyNames)
        {
            if (TryProperty(root, name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray();
            }
        }

        return Array.Empty<JsonElement>();
    }

    private static JsonElement? LatestGeometry(JsonElement item)
    {
        if (!TryProperty(item, "geometry", out var geometry)
            || geometry.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? latest = null;
        DateTimeOffset? latestDate = null;
        foreach (var candidate in geometry.EnumerateArray())
        {
            var date = DateValue(candidate, "date");
            if (latest is null
                || (date is not null && (latestDate is null || date > latestDate)))
            {
                latest = candidate;
                latestDate = date ?? latestDate;
            }
        }

        return latest;
    }

    private static (double? Latitude, double? Longitude) PointCoordinates(JsonElement geometry)
    {
        if (!TryProperty(geometry, "coordinates", out var coordinates)
            || coordinates.ValueKind != JsonValueKind.Array
            || coordinates.GetArrayLength() < 2)
        {
            return (null, null);
        }

        // GeoJSON order is [longitude, latitude].
        var longitude = Number(coordinates[0]);
        var latitude = Number(coordinates[1]);
        return (latitude, longitude);
    }

    private static (double? Latitude, double? Longitude) Coordinates(XElement item)
    {
        var latitude = item.Descendants()
            .FirstOrDefault(element => IsNamed(element, "lat"))
            ?.Value;
        var longitude = item.Descendants()
            .FirstOrDefault(element => IsNamed(element, "long") || IsNamed(element, "lon"))
            ?.Value;

        return (ParseDouble(latitude), ParseDouble(longitude));
    }

    private static string NormalizeGdacsType(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "event";
        }

        return eventType.Trim().ToUpperInvariant() switch
        {
            "EQ" => "earthquake",
            "TC" => "cyclone",
            "FL" => "flood",
            "WF" => "wildfire",
            "VO" => "volcano",
            "DR" => "drought",
            "LS" => "landslide",
            "TS" => "tsunami",
            _ => eventType.Trim().ToLowerInvariant(),
        };
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

    private static string? NestedString(JsonElement element, string parentName, string childName)
    {
        if (!TryProperty(element, parentName, out var parent))
        {
            return null;
        }

        if (parent.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in parent.EnumerateArray())
            {
                var value = StringValue(child, childName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        return StringValue(parent, childName);
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

    private static double? DoubleValue(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryProperty(element, name, out var value) && Number(value) is { } number)
            {
                return number;
            }
        }

        return null;
    }

    private static long? LongValue(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryProperty(element, name, out var value) && Number(value) is { } number)
            {
                return (long)number;
            }
        }

        return null;
    }

    private static DateTimeOffset? DateValue(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryProperty(element, name, out var value) && DateValue(value) is { } date)
            {
                return date;
            }
        }

        return null;
    }

    private static DateTimeOffset? DateValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return ParseDate(element.GetString());
        }

        return element.ValueKind == JsonValueKind.Number ? UnixMilliseconds(element) : null;
    }

    private static DateTimeOffset? UnixMilliseconds(JsonElement element, string propertyName) =>
        TryProperty(element, propertyName, out var value) ? UnixMilliseconds(value) : null;

    private static DateTimeOffset? UnixMilliseconds(JsonElement element)
    {
        if (Number(element) is not { } milliseconds)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static double? Number(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
        {
            return number;
        }

        return ParseDouble(element.ValueKind == JsonValueKind.String ? element.GetString() : null);
    }

    private static double? ParseDouble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(
            text.Trim().Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                text.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return null;
        }

        return parsed.ToUniversalTime();
    }

    private static bool IsNamed(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => IsNamed(element, localName));

    private static string? ChildValue(XElement parent, string localName)
    {
        var value = Child(parent, localName)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static double? ChildDouble(XElement parent, string localName) =>
        ParseDouble(ChildValue(parent, localName));

    private static string? AttributeOrChildValue(XElement element, string name)
    {
        var attribute = element.Attributes()
            .FirstOrDefault(candidate => string.Equals(candidate.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();
        if (!string.IsNullOrEmpty(attribute))
        {
            return attribute;
        }

        return ChildValue(element, name);
    }

    private static double? AttributeOrChildDouble(XElement element, string name) =>
        ParseDouble(AttributeOrChildValue(element, name));
}
