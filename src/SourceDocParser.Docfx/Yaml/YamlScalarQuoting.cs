// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Pure YAML-syntax predicates that decide whether a plain scalar must
/// be quoted to round-trip correctly. Lifted out of
/// <see cref="DocfxYamlBuilderExtensions"/> so the rules -- reserved
/// indicators, boolean / null tokens, and the context-sensitive
/// <c>:</c>+space and space+<c>#</c> terminators -- read at problem-domain
/// level and can be unit-tested without going through a full page render.
/// </summary>
internal static class YamlScalarQuoting
{
    /// <summary>Leading plain-scalar indicator characters that force quoting.</summary>
    private const string ReservedLeadingIndicators = " \t-?:,[]{}#&*!|>'\"%@`";

    /// <summary>Reserved YAML 1.1 boolean and null tokens that must stay quoted to round-trip as strings.</summary>
    private static readonly HashSet<string> _reservedYamlTokens =
    [
        "true",
        "false",
        "null",
        "True",
        "False",
        "Null",
        "TRUE",
        "FALSE",
        "NULL",
        "~",
        "yes",
        "no",
    ];

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
        ReservedLeadingIndicators.AsSpan().Contains(first);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> matches one of
    /// the YAML 1.1 boolean / null reserved tokens that must be quoted
    /// to round-trip as a string.
    /// </summary>
    /// <param name="value">Scalar to test.</param>
    /// <returns><see langword="true"/> when the scalar matches a reserved token.</returns>
    public static bool IsReservedYamlToken(string value) => _reservedYamlTokens.Contains(value);

    /// <summary>
    /// Walks <paramref name="value"/> once looking for any character
    /// that would terminate or otherwise disrupt a YAML plain scalar.
    /// <paramref name="prev"/> and <paramref name="next"/> let the
    /// scanner check the context-sensitive <c>:</c>+space and
    /// space+<c>#</c> rules across composite boundaries -- pass <c>'\0'</c>
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
            var current = value[i];
            if (TerminatesPlainScalar(current))
            {
                return true;
            }

            if (current is ':' && HasPlainScalarTerminatingFollower(value, i, next))
            {
                return true;
            }

            if (current is '#' && HasYamlCommentPrefix(value, i, prev))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether a character is always invalid inside an unquoted YAML plain scalar.
    /// </summary>
    /// <param name="value">Character to inspect.</param>
    /// <returns><see langword="true"/> when the character forces quoting.</returns>
    private static bool TerminatesPlainScalar(char value) => value is < ' ' or '"' or '\\';

    /// <summary>
    /// Returns whether a colon is followed by whitespace or end-of-segment, terminating a plain scalar.
    /// </summary>
    /// <param name="value">Segment being scanned.</param>
    /// <param name="index">Index of the colon within the segment.</param>
    /// <param name="next">Character immediately after the segment, or <c>'\0'</c>.</param>
    /// <returns><see langword="true"/> when the colon forces quoting.</returns>
    private static bool HasPlainScalarTerminatingFollower(in ReadOnlySpan<char> value, int index, char next)
    {
        var following = index == value.Length - 1 ? next : value[index + 1];
        return following is ' ' or '\t' or '\0';
    }

    /// <summary>
    /// Returns whether a hash sign is preceded by whitespace, starting a YAML comment.
    /// </summary>
    /// <param name="value">Segment being scanned.</param>
    /// <param name="index">Index of the hash sign within the segment.</param>
    /// <param name="prev">Character immediately before the segment, or <c>'\0'</c>.</param>
    /// <returns><see langword="true"/> when the hash sign forces quoting.</returns>
    private static bool HasYamlCommentPrefix(in ReadOnlySpan<char> value, int index, char prev)
    {
        var preceding = index is 0 ? prev : value[index - 1];
        return preceding is ' ' or '\t';
    }
}
