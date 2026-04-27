// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// Pure span-based query-string parser used by the URL rewriters to
/// pluck named parameters out of a raw URL's query region. Lifted out
/// of <see cref="SourceUrlRewriter"/> so the extraction logic reads at
/// problem-domain level and is testable in isolation.
/// </summary>
internal static class QueryParameterParser
{
    /// <summary>The query parameter pair separator character.</summary>
    private const char PairSeparator = '&';

    /// <summary>The query parameter value separator character.</summary>
    private const char ValueSeparator = '=';

    /// <summary>
    /// Returns the value of the first query parameter named
    /// <paramref name="name"/>, or <see langword="null"/> when the
    /// parameter is not present. The match is case-insensitive on the
    /// name; the value is returned verbatim.
    /// </summary>
    /// <param name="query">Query-string region (without the leading <c>?</c>).</param>
    /// <param name="name">Parameter name to look up.</param>
    /// <returns>The parameter value, or <see langword="null"/> when absent.</returns>
    public static string? Extract(in ReadOnlySpan<char> query, string name)
    {
        var current = query;
        while (!current.IsEmpty)
        {
            var ampIdx = current.IndexOf(PairSeparator);
            var pair = ampIdx < 0 ? current : current[..ampIdx];
            var eqIdx = pair.IndexOf(ValueSeparator);
            if (eqIdx > 0 && pair[..eqIdx].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return pair[(eqIdx + 1)..].ToString();
            }

            if (ampIdx < 0)
            {
                break;
            }

            current = current[(ampIdx + 1)..];
        }

        return null;
    }
}
