// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Pure YAML-syntax predicates that decide whether a plain scalar must
/// be quoted to round-trip correctly. Lifted out of
/// <see cref="DocfxYamlBuilderExtensions"/> so the rules — reserved
/// indicators, boolean / null tokens, and the context-sensitive
/// <c>:</c>+space and space+<c>#</c> terminators — read at problem-domain
/// level and can be unit-tested without going through a full page render.
/// </summary>
internal static class YamlScalarQuoting
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> would be
    /// misparsed by a YAML reader without quoting.
    /// </summary>
    /// <param name="value">Scalar to inspect (must be non-empty).</param>
    /// <returns><see langword="true"/> when the scalar must be quoted.</returns>
    public static bool NeedsQuoting(string value) =>
        HasReservedLeadingIndicator(value[0])
        || IsReservedYamlToken(value)
        || ScanForTerminators(value.AsSpan(), prev: '\0', next: '\0');

    /// <summary>
    /// Composite-aware variant for the <c>left + separator + right</c>
    /// shape used by <c>AppendQualifiedScalar</c>. Walks both halves once
    /// with boundary-aware lookups so the cold quoted-fallback path
    /// doesn't pay for two separate scans, and skips the reserved-token
    /// check entirely (a composite with a separator in the middle can't
    /// equal a single reserved token like <c>null</c>).
    /// </summary>
    /// <param name="left">Left half of the composite (must be non-empty).</param>
    /// <param name="separator">Joining character.</param>
    /// <param name="right">Right half of the composite (must be non-empty).</param>
    /// <returns><see langword="true"/> when the composite scalar must be quoted.</returns>
    public static bool CompositeNeedsQuoting(string left, char separator, string right) =>
        HasReservedLeadingIndicator(left[0])
        || ScanForTerminators(left.AsSpan(), prev: '\0', next: separator)
        || ScanForTerminators(right.AsSpan(), prev: separator, next: '\0');

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="first"/> is one of the
    /// YAML reserved leading indicators that force quoting on a plain
    /// scalar.
    /// </summary>
    /// <param name="first">First character of the scalar.</param>
    /// <returns><see langword="true"/> when the character is reserved.</returns>
    public static bool HasReservedLeadingIndicator(char first) =>
        first is ' ' or '\t' or '-' or '?' or ':' or ',' or '[' or ']' or '{' or '}' or '#' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`';

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> matches one of
    /// the YAML 1.1 boolean / null reserved tokens that must be quoted
    /// to round-trip as a string.
    /// </summary>
    /// <param name="value">Scalar to test.</param>
    /// <returns><see langword="true"/> when the scalar matches a reserved token.</returns>
    public static bool IsReservedYamlToken(string value) =>
        value is "true" or "false" or "null" or "True" or "False" or "Null"
            or "TRUE" or "FALSE" or "NULL" or "~" or "yes" or "no";

    /// <summary>
    /// Walks <paramref name="value"/> once looking for any character
    /// that would terminate or otherwise disrupt a YAML plain scalar.
    /// <paramref name="prev"/> and <paramref name="next"/> let the
    /// scanner check the context-sensitive <c>:</c>+space and
    /// space+<c>#</c> rules across composite boundaries — pass <c>'\0'</c>
    /// when there's no boundary character to consult.
    /// </summary>
    /// <param name="value">Scalar segment to scan.</param>
    /// <param name="prev">Character immediately before the segment, or <c>'\0'</c> for "none".</param>
    /// <param name="next">Character immediately after the segment, or <c>'\0'</c> for "none".</param>
    /// <returns><see langword="true"/> when the segment contains a terminator.</returns>
    public static bool ScanForTerminators(in ReadOnlySpan<char> value, char prev, char next)
    {
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case < ' ' or '"' or '\\':
                    return true;
                case ':':
                    {
                        var following = i == value.Length - 1 ? next : value[i + 1];
                        if (following is ' ' or '\t' or '\0')
                        {
                            return true;
                        }

                        break;
                    }

                case '#':
                    {
                        var preceding = i is 0 ? prev : value[i - 1];
                        if (preceding is ' ' or '\t')
                        {
                            return true;
                        }

                        break;
                    }
            }
        }

        return false;
    }
}
