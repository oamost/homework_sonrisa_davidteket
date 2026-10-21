namespace WorldAlerts.Api.Collection;

public static class SourceCadence
{
    /// <summary>A source override may slow polling but may not exceed the family request rate.</summary>
    public static TimeSpan Effective(TimeSpan familyInterval, int? sourceMinutes) =>
        sourceMinutes is > 0 ? TimeSpan.FromMinutes(Math.Max(familyInterval.TotalMinutes, sourceMinutes.Value))
            : familyInterval;
}
