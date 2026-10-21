using System.Text;

namespace WorldAlerts.Api.Scoring;

/// <summary>Deterministic, culture-independent text helpers used by the scorers.</summary>
internal static class ScoringText
{
    /// <summary>
    /// Lower-cases text and collapses every non-alphanumeric run to one space so
    /// source ids such as <c>bbc-world</c> and names such as <c>BBC World</c>
    /// compare consistently.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>Returns true when the normalized value contains the normalized token sequence.</summary>
    public static bool ContainsTokenSequence(string normalizedValue, string normalizedSequence)
    {
        if (normalizedValue.Length == 0 || normalizedSequence.Length == 0)
        {
            return false;
        }

        if (string.Equals(normalizedValue, normalizedSequence, StringComparison.Ordinal))
        {
            return true;
        }

        return (" " + normalizedValue + " ").Contains(
            " " + normalizedSequence + " ",
            StringComparison.Ordinal);
    }

    /// <summary>Returns the first approved keyword found in the normalized value.</summary>
    public static string? FindTokenSequence(string normalizedValue, IEnumerable<string> normalizedSequences)
    {
        foreach (var sequence in normalizedSequences)
        {
            if (ContainsTokenSequence(normalizedValue, sequence))
            {
                return sequence;
            }
        }

        return null;
    }
}
