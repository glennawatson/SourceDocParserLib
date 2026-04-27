// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Renders a YAML literal-block scalar (<c>key |-</c> followed by an
/// indented body). Lifted out of <see cref="DocfxYamlBuilderExtensions"/>
/// so the indent calculation and per-line emission read at
/// problem-domain level and are testable in isolation.
/// </summary>
internal static class YamlLiteralBlockFormatter
{
    /// <summary>
    /// Writes <paramref name="value"/> as a literal block under
    /// <paramref name="prefix"/>. The body is indented by the prefix's
    /// leading-space count plus two — the standard YAML literal-block
    /// continuation indent.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="prefix">Key + colon + space prefix; leading spaces drive the body indent.</param>
    /// <param name="value">Body text containing at least one newline.</param>
    /// <returns>The same <paramref name="sb"/>, for chaining.</returns>
    public static StringBuilder Format(StringBuilder sb, string prefix, string value)
    {
        var indentLength = ComputeIndentLength(prefix);
        var indent = new string(' ', indentLength + 2);
        var key = prefix.AsSpan(indentLength).TrimEnd();
        sb.Append(' ', indentLength).Append(key).Append(" |-\n");

        foreach (var line in value.AsSpan().EnumerateLines())
        {
            sb.Append(indent).Append(line).AppendLine();
        }

        return sb;
    }

    /// <summary>
    /// Returns the leading-space count of <paramref name="prefix"/> —
    /// this is the indent of the key line, and the body is indented
    /// two further spaces.
    /// </summary>
    /// <param name="prefix">Key + colon + space prefix.</param>
    /// <returns>The number of leading spaces; the full prefix length when the prefix is all spaces.</returns>
    public static int ComputeIndentLength(string prefix)
    {
        var span = prefix.AsSpan();
        var firstNonSpace = span.IndexOfAnyExcept(' ');
        return firstNonSpace < 0 ? span.Length : firstNonSpace;
    }
}
