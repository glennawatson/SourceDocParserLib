// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;
using SourceDocParser.Common;

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// UID-string toolkit shared across the docfx YAML emitter -- strips
/// the Roslyn doc-comment prefix, walks brace-delimited generic
/// regions, derives the parent namespace from a bare type name, and
/// synthesises the docfx-style <c>fullName</c> from a UID. Lifted out
/// of <see cref="DocfxReferenceEnricher"/> so the UID-parsing rules
/// read at problem-domain level and are testable in isolation.
/// </summary>
internal static class UidNormalization
{
    /// <summary>Strips the leading <c>T:</c> / <c>M:</c> / etc. prefix from a UID.</summary>
    /// <param name="uid">The full UID.</param>
    /// <returns>The bare name, without any single-letter prefix.</returns>
    public static string StripPrefix(string uid) => CommentIdPrefix.Strip(uid);

    /// <summary>Strips the trailing <c>`N</c> arity suffix from a bare type-name segment.</summary>
    /// <param name="head">Bare type-name segment (may end in arity backtick).</param>
    /// <returns>The name with the suffix removed when present.</returns>
    public static string StripArityBacktick(string head)
    {
        var tickIdx = head.LastIndexOf('`');
        return tickIdx > 0 ? head[..tickIdx] : head;
    }

    /// <summary>Strips the namespace from a bare type name to get its parent (namespace) part.</summary>
    /// <param name="bareName">The bare type name.</param>
    /// <returns>The parent namespace, or an empty string when the name is unqualified.</returns>
    public static string ParentOf(string bareName)
    {
        var lastDot = bareName.LastIndexOf('.');
        if (lastDot < 0)
        {
            return string.Empty;
        }

        // Constructed generics have the form Foo`1{Bar} -- only walk
        // up to the brace boundary so we don't lop off type-arg dots.
        var braceIdx = bareName.IndexOf('{', StringComparison.Ordinal);
        if (braceIdx >= 0 && braceIdx < lastDot)
        {
            lastDot = bareName.LastIndexOf('.', braceIdx - 1);
            if (lastDot < 0)
            {
                return string.Empty;
            }
        }

        return bareName[..lastDot];
    }

    /// <summary>
    /// Converts a constructed-generic UID like <c>T:Foo{Bar}</c> into
    /// its open-generic form <c>T:Foo`1</c>. Counts the top-level type
    /// args inside the brace region to reconstruct the arity backtick
    /// the walker omits.
    /// </summary>
    /// <param name="uid">The reference UID.</param>
    /// <returns>The open-generic UID; the input unchanged when not generic.</returns>
    public static string ToOpenGenericUid(string uid)
    {
        var braceIdx = uid.IndexOf('{', StringComparison.Ordinal);
        if (braceIdx < 0)
        {
            return uid;
        }

        var bareHead = uid[..braceIdx];
        if (bareHead.Contains('`', StringComparison.Ordinal))
        {
            return bareHead;
        }

        var arity = CountTopLevelArgsInUidBraces(uid, braceIdx);
        return bareHead + "`" + arity.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Synthesises the docfx-style <c>fullName</c> from a bare UID
    /// (no <c>T:</c> prefix). Non-generic UIDs flow through almost
    /// unchanged -- only BCL primitive aliasing is applied. Generic
    /// UIDs replace the <c>{...}</c> brace region with the docfx
    /// <c>...</c> form, recursively expanding any nested args
    /// and stripping the <c>`N</c> arity suffix from the base type.
    /// All segment names stay fully namespaced.
    /// </summary>
    /// <param name="bareName">UID with the <c>T:</c> prefix already stripped.</param>
    /// <returns>The fully-qualified docfx-style name.</returns>
    public static string SynthesiseFullName(string bareName)
    {
        var braceIdx = bareName.IndexOf('{', StringComparison.Ordinal);
        if (braceIdx < 0)
        {
            return BclTypeAliases.ToKeyword(bareName, bareName);
        }

        var head = bareName[..braceIdx];
        var argRegion = bareName[(braceIdx + 1)..^1];
        var baseName = StripArityBacktick(head);

        var sb = new StringBuilder(bareName.Length + 8);
        sb.Append(BclTypeAliases.ToKeyword(baseName, baseName)).Append('<');
        var argSegments = SplitTopLevelArgs(argRegion, '{', '}');
        for (var i = 0; i < argSegments.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(SynthesiseFullName(argSegments[i].Trim()));
        }

        return sb.Append('>').ToString();
    }

    /// <summary>Counts top-level type-argument tokens inside a brace region of a UID.</summary>
    /// <param name="uid">The full UID containing a brace region.</param>
    /// <param name="openBraceIdx">Index of the opening brace.</param>
    /// <returns>The number of top-level commas inside the braces, plus one.</returns>
    public static int CountTopLevelArgsInUidBraces(string uid, int openBraceIdx)
    {
        var depth = 0;
        var count = 1;
        for (var i = openBraceIdx; i < uid.Length; i++)
        {
            var c = uid[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
            }
            else if (c == ',' && depth == 1)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Splits a comma-separated type-argument list, ignoring commas inside nested bracket pairs.</summary>
    /// <param name="region">The comma-separated arg list (without the outer brackets).</param>
    /// <param name="open">Opening bracket character (<c>&lt;</c> for display names, <c>{</c> for UIDs).</param>
    /// <param name="close">Closing bracket character.</param>
    /// <returns>The pieces in source order.</returns>
    public static List<string> SplitTopLevelArgs(string region, char open, char close)
    {
        var depth = 0;
        var start = 0;
        var pieces = new List<string>();
        for (var i = 0; i < region.Length; i++)
        {
            var c = region[i];
            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                pieces.Add(region[start..i]);
                start = i + 1;
            }
        }

        pieces.Add(region[start..]);
        return pieces;
    }

    /// <summary>Counts top-level type arguments inside a comma-separated arg region.</summary>
    /// <param name="region">The arg region (without the outer brackets).</param>
    /// <param name="open">Opening bracket character.</param>
    /// <param name="close">Closing bracket character.</param>
    /// <returns>The number of top-level commas plus one.</returns>
    public static int CountTopLevelArgs(string region, char open, char close)
    {
        var depth = 0;
        var count = 1;
        for (var i = 0; i < region.Length; i++)
        {
            var c = region[i];
            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                count++;
            }
        }

        return count;
    }
}
