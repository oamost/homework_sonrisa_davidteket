using System.Text;

namespace WorldAlerts.Api.Dedupe;

/// <summary>
/// Normalizes provenance URLs for identity fallbacks and conservative
/// cross-source news reconciliation. The result is a comparison key, not a URL
/// to fetch: the scheme and host are lowercased, a default port and fragment are
/// dropped, a trailing slash is removed, tracking parameters are discarded and
/// the remaining query parameters are sorted.
/// </summary>
public static class DedupeUrl
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid",
        "gclid",
        "dclid",
        "msclkid",
        "yclid",
        "mc_cid",
        "mc_eid",
        "igshid",
        "_hsenc",
        "_hsmi",
        "vero_id",
        "oly_anon_id",
        "oly_enc_id",
        "ref",
        "ref_src",
        "ref_url",
        "source",
        "campaign",
        "campaign_id",
        "campaignid",
        "cmp",
        "s_cid",
        "spm",
        "scm",
        "ns_campaign",
        "ns_mchannel",
        "ns_source",
        "at_medium",
        "at_campaign",
        "ito",
        "smid",
        "partner",
    };

    private static readonly string[] TrackingPrefixes =
    {
        "utm_",
        "pk_",
        "mtm_",
        "hsa_",
    };

    /// <summary>
    /// Returns a stable comparison key. Non-absolute or unparseable values are
    /// trimmed and lowercased so that identical strings still compare equal.
    /// </summary>
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed.ToLowerInvariant();
        }

        var builder = new StringBuilder(trimmed.Length);
        builder.Append(uri.Scheme.ToLowerInvariant());
        builder.Append("://");
        builder.Append(uri.Host.ToLowerInvariant());

        if (!uri.IsDefaultPort)
        {
            builder.Append(':');
            builder.Append(uri.Port);
        }

        var path = uri.AbsolutePath;
        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        builder.Append(path.ToLowerInvariant());

        var query = BuildQuery(uri.Query);
        if (query.Length > 0)
        {
            builder.Append('?');
            builder.Append(query);
        }

        return builder.ToString();
    }

    private static string BuildQuery(string query)
    {
        if (string.IsNullOrEmpty(query) || query == "?")
        {
            return string.Empty;
        }

        var parameters = new List<KeyValuePair<string, string>>();
        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf('=');
            var name = separator >= 0 ? segment[..separator] : segment;
            var value = separator >= 0 ? segment[(separator + 1)..] : string.Empty;

            name = Uri.UnescapeDataString(name).Trim();
            if (name.Length == 0 || IsTrackingParameter(name))
            {
                continue;
            }

            parameters.Add(new KeyValuePair<string, string>(
                name.ToLowerInvariant(),
                Uri.UnescapeDataString(value)));
        }

        if (parameters.Count == 0)
        {
            return string.Empty;
        }

        parameters.Sort(static (left, right) =>
        {
            var byName = string.CompareOrdinal(left.Key, right.Key);
            return byName != 0 ? byName : string.CompareOrdinal(left.Value, right.Value);
        });

        var builder = new StringBuilder();
        foreach (var parameter in parameters)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(parameter.Key);
            builder.Append('=');
            builder.Append(parameter.Value);
        }

        return builder.ToString();
    }

    private static bool IsTrackingParameter(string name)
    {
        if (TrackingParameters.Contains(name))
        {
            return true;
        }

        foreach (var prefix in TrackingPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
