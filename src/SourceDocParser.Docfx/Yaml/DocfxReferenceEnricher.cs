// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

namespace SourceDocParser.Docfx;

/// <summary>
/// Renders one entry of a docfx <c>references:</c> block with the
/// fields docfx itself populates: <c>parent</c>, <c>isExternal</c>,
/// <c>href</c>, and the generic-form <c>spec.csharp</c> token list.
/// Classification is driven by a set of internal UIDs (types this
/// emitter wrote pages for) — anything outside that set is treated
/// as external; BCL types additionally route to Microsoft Learn.
/// </summary>
internal static class DocfxReferenceEnricher
{
    /// <summary>Microsoft Learn URL prefix for BCL type pages.</summary>
    private const string LearnBaseUrl = "https://learn.microsoft.com/dotnet/api/";

    /// <summary>Namespace prefixes treated as Microsoft-hosted documentation.</summary>
    private static readonly string[] _bclNamespacePrefixes = ["System", "Microsoft"];

    /// <summary>
    /// Writes one fully-populated reference entry to <paramref name="sb"/>.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="reference">Reference to emit.</param>
    /// <param name="internalUids">UIDs of types emitted by this run; drives <c>isExternal</c> and <c>href</c>.</param>
    /// <returns>The builder for chaining.</returns>
    public static StringBuilder AppendEnrichedReference(
        StringBuilder sb,
        ApiTypeReference reference,
        HashSet<string> internalUids)
    {
        var uid = reference is { Uid: [_, ..] u } ? u : "T:" + reference.DisplayName;
        var commentId = uid;
        var displayName = reference.DisplayName;
        var bareName = StripPrefix(uid);
        var openGenericUid = ToOpenGenericUid(uid);
        var isInternal = internalUids.Contains(uid) || internalUids.Contains(openGenericUid);
        var parent = ParentOf(bareName);

        sb.Append("- uid: ").AppendScalar(DocfxCommentId.ToUid(uid)).AppendLine()
            .Append("  commentId: ").AppendScalar(commentId).AppendLine();

        if (parent is { Length: > 0 })
        {
            sb.Append("  parent: ").AppendScalar(parent).AppendLine();
        }

        // Constructed generics get a definition pointer back to the
        // open-generic uid so docfx can resolve the page link.
        if (!string.Equals(uid, openGenericUid, StringComparison.Ordinal))
        {
            sb.Append("  definition: ").AppendScalar(openGenericUid).AppendLine();
        }

        AppendIsExternal(sb, isInternal, indent: "  ");
        var href = ResolveHref(uid, openGenericUid, bareName, isInternal);
        AppendHref(sb, href, indent: "  ");

        sb.Append("  name: ").AppendScalar(displayName).AppendLine()
            .Append("  nameWithType: ").AppendScalar(displayName).AppendLine()
            .Append("  fullName: ").AppendScalar(displayName).AppendLine();

        if (displayName.AsSpan().IndexOfAny('<', '>') >= 0)
        {
            AppendSpecCsharp(sb, displayName, openGenericUid, internalUids);
        }

        return sb;
    }

    /// <summary>Strips the leading <c>T:</c> / <c>M:</c> / etc. prefix from a UID.</summary>
    /// <param name="uid">The full UID.</param>
    /// <returns>The bare name, without any single-letter prefix.</returns>
    private static string StripPrefix(string uid) =>
        uid is [_, ':', ..] ? uid[2..] : uid;

    /// <summary>Strips the namespace from a bare type name to get its parent (namespace) part.</summary>
    /// <param name="bareName">The bare type name.</param>
    /// <returns>The parent namespace, or an empty string when the name is unqualified.</returns>
    private static string ParentOf(string bareName)
    {
        var lastDot = bareName.LastIndexOf('.');
        if (lastDot < 0)
        {
            return string.Empty;
        }

        // Constructed generics have the form Foo`1{Bar} — only walk
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
    /// Converts a constructed-generic UID like <c>T:Foo{Bar}</c>
    /// into its open-generic form <c>T:Foo`1</c>. Counts the
    /// top-level type args inside the brace region to reconstruct
    /// the arity backtick the walker omits, matching the convention
    /// used by mkdocs-autorefs UIDs in the Zensical layer.
    /// </summary>
    /// <param name="uid">The reference UID.</param>
    /// <returns>The open-generic UID; the input unchanged when not generic.</returns>
    private static string ToOpenGenericUid(string uid)
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

    /// <summary>Counts top-level type-argument tokens inside a brace region of a UID.</summary>
    /// <param name="uid">The full UID containing a brace region.</param>
    /// <param name="openBraceIdx">Index of the opening brace.</param>
    /// <returns>The number of top-level commas inside the braces, plus one.</returns>
    private static int CountTopLevelArgsInUidBraces(string uid, int openBraceIdx)
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

    /// <summary>
    /// Picks the <c>href</c> value for a reference: a local page link
    /// for internal types, a Microsoft Learn URL for BCL types, or
    /// nothing for unknown externals.
    /// </summary>
    /// <param name="uid">Reference UID.</param>
    /// <param name="openGenericUid">UID with the type-argument list stripped.</param>
    /// <param name="bareName">Bare name (UID without the prefix).</param>
    /// <param name="isInternal">Whether the type was emitted by this run.</param>
    /// <returns>The href value, or empty when none applies.</returns>
    private static string ResolveHref(string uid, string openGenericUid, string bareName, bool isInternal)
    {
        if (isInternal)
        {
            // Local type: link at the open-generic page (Foo`1.html), since
            // constructed generics share the same page in docfx.
            var stem = StripPrefix(openGenericUid).Replace('`', '-');
            return stem + ".html";
        }

        if (!StartsWithBclPrefix(bareName))
        {
            return string.Empty;
        }

        // Microsoft Learn URLs lowercase the type and rewrite arity
        // backticks to hyphens — System.Action`1 → system.action-1.
        var slug = StripPrefix(openGenericUid).ToLower(CultureInfo.InvariantCulture).Replace('`', '-');
        return LearnBaseUrl + slug;
    }

    /// <summary>Tests whether <paramref name="bareName"/> starts with one of the BCL namespace prefixes.</summary>
    /// <param name="bareName">Bare type name.</param>
    /// <returns>True for names rooted at <c>System.</c> or <c>Microsoft.</c>.</returns>
    private static bool StartsWithBclPrefix(string bareName)
    {
        for (var i = 0; i < _bclNamespacePrefixes.Length; i++)
        {
            var prefix = _bclNamespacePrefixes[i];
            if (bareName.Length > prefix.Length
                && bareName.StartsWith(prefix, StringComparison.Ordinal)
                && bareName[prefix.Length] == '.')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes the <c>spec.csharp</c> token list for a generic reference,
    /// splitting the display name into linkable base type + bracket
    /// punctuation + per-arg entries. Each component is rendered as a
    /// sub-item with its own <c>uid</c>/<c>name</c>/<c>isExternal</c>/<c>href</c>
    /// so docfx can hyperlink each token independently.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="displayName">Display form of the generic reference (e.g. <c>IObservable&lt;int&gt;</c>).</param>
    /// <param name="openGenericUid">Open-generic UID for the base type token.</param>
    /// <param name="internalUids">Set used to classify each token as internal or external.</param>
    private static void AppendSpecCsharp(
        StringBuilder sb,
        string displayName,
        string openGenericUid,
        HashSet<string> internalUids)
    {
        var ltIdx = displayName.IndexOf('<', StringComparison.Ordinal);
        if (ltIdx <= 0)
        {
            return;
        }

        var baseName = displayName[..ltIdx];
        var argsRegion = displayName[(ltIdx + 1)..^1];

        sb.AppendLine("  spec.csharp:");
        AppendSpecComponent(sb, openGenericUid, baseName, internalUids);
        sb.AppendLine("  - name: <");
        var args = SplitTopLevelArgs(argsRegion);
        for (var i = 0; i < args.Count; i++)
        {
            AppendArgWithSeparator(sb, args[i], i, args.Count, internalUids);
        }

        sb.AppendLine("  - name: '>'");
    }

    /// <summary>Renders one type-arg of a spec list and the comma separator between adjacent entries.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="arg">The type-arg display form.</param>
    /// <param name="index">Zero-based position in the parent list.</param>
    /// <param name="count">Total count of args in the parent list.</param>
    /// <param name="internalUids">Classification set.</param>
    private static void AppendArgWithSeparator(StringBuilder sb, string arg, int index, int count, HashSet<string> internalUids)
    {
        AppendSpecArg(sb, arg, internalUids);
        if (index >= count - 1)
        {
            return;
        }

        sb.AppendLine("  - name: ', '");
    }

    /// <summary>Writes one type-token entry inside a <c>spec.csharp</c> list.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="uid">Token UID (open-generic form for generic bases).</param>
    /// <param name="name">Display name for the token.</param>
    /// <param name="internalUids">Classification set.</param>
    private static void AppendSpecComponent(StringBuilder sb, string uid, string name, HashSet<string> internalUids)
    {
        var isInternal = internalUids.Contains(uid);
        var href = ResolveHref(uid, ToOpenGenericUid(uid), StripPrefix(uid), isInternal);
        sb.Append("  - uid: ").AppendScalar(uid).AppendLine()
            .Append("    name: ").AppendScalar(name).AppendLine();
        AppendIsExternal(sb, isInternal, indent: "    ");
        AppendHref(sb, href, indent: "    ");
    }

    /// <summary>Writes <c>isExternal: true</c> at the requested indent when the symbol isn't internal; no-op otherwise.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="isInternal">Whether the reference points at a type emitted by this run.</param>
    /// <param name="indent">Leading indent for the line.</param>
    private static void AppendIsExternal(StringBuilder sb, bool isInternal, string indent)
    {
        if (isInternal)
        {
            return;
        }

        sb.Append(indent).AppendLine("isExternal: true");
    }

    /// <summary>Writes <c>href: …</c> at the requested indent when the value is non-empty.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="href">Pre-resolved href value.</param>
    /// <param name="indent">Leading indent for the line.</param>
    private static void AppendHref(StringBuilder sb, string href, string indent)
    {
        if (href is not [_, ..])
        {
            return;
        }

        sb.Append(indent).Append("href: ").AppendScalar(href).AppendLine();
    }

    /// <summary>
    /// Renders one type-arg of a constructed generic — recurses into
    /// nested generics so <c>Func&lt;IObservable&lt;int&gt;&gt;</c>
    /// produces a nested spec list rather than a flat string.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="arg">The display form of one type argument.</param>
    /// <param name="internalUids">Classification set.</param>
    private static void AppendSpecArg(StringBuilder sb, string arg, HashSet<string> internalUids)
    {
        var trimmed = arg.Trim();
        var nestedLt = trimmed.IndexOf('<', StringComparison.Ordinal);
        if (nestedLt < 0)
        {
            // Plain leaf type: emit a single component. The display
            // form may be unqualified (e.g. "int") so we synthesise a
            // T: UID assuming primitive aliases map to System.* types.
            var uid = "T:" + LiftPrimitive(trimmed);
            AppendSpecComponent(sb, uid, trimmed, internalUids);
            return;
        }

        var baseName = trimmed[..nestedLt];
        var argRegion = trimmed[(nestedLt + 1)..^1];
        var arity = CountTopLevelArgs(argRegion);
        var openUid = "T:" + baseName + "`" + arity.ToString(CultureInfo.InvariantCulture);
        AppendSpecComponent(sb, openUid, baseName, internalUids);
        sb.AppendLine("  - name: <");
        var nestedArgs = SplitTopLevelArgs(argRegion);
        for (var i = 0; i < nestedArgs.Count; i++)
        {
            AppendArgWithSeparator(sb, nestedArgs[i], i, nestedArgs.Count, internalUids);
        }

        sb.AppendLine("  - name: '>'");
    }

    /// <summary>Splits a comma-separated type-argument list, ignoring commas inside nested angle brackets.</summary>
    /// <param name="region">The comma-separated arg list (without the outer angle brackets).</param>
    /// <returns>The pieces in source order.</returns>
    private static List<string> SplitTopLevelArgs(string region)
    {
        var depth = 0;
        var start = 0;
        var pieces = new List<string>();
        for (var i = 0; i < region.Length; i++)
        {
            var c = region[i];
            if (c == '<')
            {
                depth++;
            }
            else if (c == '>')
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
    /// <param name="region">The arg region (without the outer angle brackets).</param>
    /// <returns>The number of top-level commas plus one.</returns>
    private static int CountTopLevelArgs(string region)
    {
        var depth = 0;
        var count = 1;
        for (var i = 0; i < region.Length; i++)
        {
            var c = region[i];
            if (c == '<')
            {
                depth++;
            }
            else if (c == '>')
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

    /// <summary>Maps C# primitive aliases (<c>int</c>, <c>string</c>, …) to their <c>System.*</c> CLR names.</summary>
    /// <param name="name">A possibly-aliased type name.</param>
    /// <returns>The promoted CLR name, or the input unchanged when it isn't a known alias.</returns>
    private static string LiftPrimitive(string name) => name switch
    {
        "int" => "System.Int32",
        "uint" => "System.UInt32",
        "long" => "System.Int64",
        "ulong" => "System.UInt64",
        "short" => "System.Int16",
        "ushort" => "System.UInt16",
        "byte" => "System.Byte",
        "sbyte" => "System.SByte",
        "bool" => "System.Boolean",
        "char" => "System.Char",
        "string" => "System.String",
        "object" => "System.Object",
        "double" => "System.Double",
        "float" => "System.Single",
        "decimal" => "System.Decimal",
        "void" => "System.Void",
        _ => name,
    };
}
