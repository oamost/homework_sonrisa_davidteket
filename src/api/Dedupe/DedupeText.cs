using System.Text;

namespace WorldAlerts.Api.Dedupe;

/// <summary>
/// Deterministic text normalization used by identity fallbacks, fingerprints and
/// conservative cross-source reconciliation. Normalization is intentionally
/// lossy for comparison only; callers keep the original provider text elsewhere.
/// </summary>
public static class DedupeText
{
    /// <summary>
    /// Trims, lowercases with the invariant culture and collapses every run of
    /// whitespace to a single space.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Normalizes a headline for equality checks: <see cref="Normalize"/> plus
    /// removal of punctuation and symbols. This prevents quote style, dashes and
    /// trailing punctuation from creating separate fingerprints or clusters.
    /// </summary>
    public static string NormalizeTitle(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
            }
            else
            {
                // Punctuation and symbols are comparison noise, not title content.
                pendingSpace = builder.Length > 0;
            }
        }

        return builder.ToString();
    }
}
