namespace WorldAlerts.Api.Advisories;

/// <summary>
/// One cyber advisory from a source-native catalogue. The CVE id is the native
/// identity inside a source, so re-ingesting the same catalogue is idempotent.
/// </summary>
public sealed record Advisory(
    string SourceId,
    string CveId,
    string Vendor,
    string Product,
    string Name,
    DateOnly? DateAdded,
    DateOnly? DueDate,
    bool KnownRansomware,
    string Provenance,
    DateTimeOffset FetchedUtc);

/// <summary>Outcome of parsing one advisory payload.</summary>
public sealed record AdvisoryParseResult(IReadOnlyList<Advisory> Items, int Skipped);
