// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using SourceDocParser.Common;
using SourceDocParser.Model;

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Renders one entry of a docfx <c>references:</c> block with the
/// fields docfx itself populates: <c>parent</c>, <c>isExternal</c>,
/// <c>href</c>, and the generic-form <c>spec.csharp</c> token list.
/// Classification is driven by a set of internal UIDs (types this
/// emitter wrote pages for) -- anything outside that set is treated
/// as external; BCL types additionally route to Microsoft Learn.
/// UID parsing / normalization sits in <see cref="UidNormalization"/>;
/// this class focuses on YAML emission.
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
        var bareName = UidNormalization.StripPrefix(uid);
        var openGenericUid = UidNormalization.ToOpenGenericUid(uid);
        var isInternal = internalUids.Contains(uid) || internalUids.Contains(openGenericUid);
        var parent = UidNormalization.ParentOf(bareName);

        sb.Append("- uid: ").AppendScalar(CommentIdPrefix.Strip(uid)).AppendLine()
            .Append("  commentId: ").AppendScalar(commentId).AppendLine();

        if (parent is { Length: > 0 })
        {
            sb.Append("  parent: ").AppendScalar(parent).AppendLine();
        }

        // Constructed generics get a definition pointer back to the
        // open-generic uid so docfx can resolve the page link.
        // Docfx convention: definition uses the bare uid (no T: prefix).
        if (!string.Equals(uid, openGenericUid, StringComparison.Ordinal))
        {
            sb.Append("  definition: ").AppendScalar(UidNormalization.StripPrefix(openGenericUid)).AppendLine();
        }

        AppendIsExternal(sb, isInternal, indent: "  ");
        var href = ResolveHref(openGenericUid, bareName, isInternal);
        AppendHref(sb, href, indent: "  ");

        // BCL primitive class types render as their C# keyword
        // alias so the YAML matches docfx's own output (e.g. Object
        // -> object, String -> string). Value types come through the
        // walker already keyword-formatted; only class types need
        // this rewrite.
        var displayLabel = BclTypeAliases.ToKeyword(bareName, displayName);
        var fullNameLabel = UidNormalization.SynthesiseFullName(bareName);
        sb.Append("  name: ").AppendScalar(displayLabel).AppendLine()
            .Append("  nameWithType: ").AppendScalar(displayLabel).AppendLine()
            .Append("  fullName: ").AppendScalar(fullNameLabel).AppendLine();

        if (displayName.AsSpan().IndexOfAny('<', '>') >= 0)
        {
            AppendSpecCsharp(sb, uid, displayName, openGenericUid, internalUids);
        }

        return sb;
    }

    /// <summary>
    /// Picks the <c>href</c> value for a reference: a local page link
    /// for internal types, a Microsoft Learn URL for BCL types, or
    /// nothing for unknown externals.
    /// </summary>
    /// <param name="openGenericUid">UID with the type-argument list stripped.</param>
    /// <param name="bareName">Bare name (UID without the prefix).</param>
    /// <param name="isInternal">Whether the type was emitted by this run.</param>
    /// <returns>The href value, or empty when none applies.</returns>
    [SuppressMessage("Minor Code Smell", "S4040:Strings should be normalized to uppercase", Justification = "Microsoft Learn URLs are case-sensitive.")]
    private static string ResolveHref(string openGenericUid, string bareName, bool isInternal)
    {
        if (isInternal)
        {
            // Local type: link at the open-generic page (Foo`1.html), since
            // constructed generics share the same page in docfx.
            var stem = UidNormalization.StripPrefix(openGenericUid).Replace('`', '-');
            return stem + ".html";
        }

        if (!StartsWithBclPrefix(bareName))
        {
            return string.Empty;
        }

        // Microsoft Learn URLs lowercase the type and rewrite arity
        // backticks to hyphens -- System.Action`1 -> system.action-1.
        var slug = UidNormalization.StripPrefix(openGenericUid).ToLower(CultureInfo.InvariantCulture).Replace('`', '-');
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
    /// <param name="uid">Full reference UID; its brace region carries fully-qualified names for spec components.</param>
    /// <param name="displayName">Display form of the generic reference (e.g. <c>IObservable&lt;int></c>).</param>
    /// <param name="openGenericUid">Open-generic UID for the base type token.</param>
    /// <param name="internalUids">Set used to classify each token as internal or external.</param>
    private static void AppendSpecCsharp(
        StringBuilder sb,
        string uid,
        string displayName,
        string openGenericUid,
        HashSet<string> internalUids)
    {
        var ltIdx = displayName.IndexOf('<', StringComparison.Ordinal);
        var braceIdx = uid.IndexOf('{', StringComparison.Ordinal);
        if (ltIdx <= 0 || braceIdx <= 0)
        {
            return;
        }

        var baseName = displayName[..ltIdx];
        var displayArgsRegion = displayName[(ltIdx + 1)..^1];
        var uidArgsRegion = uid[(braceIdx + 1)..^1];

        sb.AppendLine("  spec.csharp:");
        AppendSpecComponent(sb, openGenericUid, baseName, internalUids);
        sb.AppendLine("  - name: <");
        var displayArgs = UidNormalization.SplitTopLevelArgs(displayArgsRegion, '<', '>');
        var uidArgs = UidNormalization.SplitTopLevelArgs(uidArgsRegion, '{', '}');
        var argCount = Math.Max(displayArgs.Count, uidArgs.Count);
        for (var i = 0; i < argCount; i++)
        {
            var displayArg = i < displayArgs.Count ? displayArgs[i] : string.Empty;
            var uidArg = i < uidArgs.Count ? uidArgs[i] : string.Empty;
            AppendArgWithSeparator(sb, uidArg, displayArg, i, argCount, internalUids);
        }

        sb.AppendLine("  - name: '>'");
    }

    /// <summary>Renders one type-arg of a spec list and the comma separator between adjacent entries.</summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="uidArg">The type-arg in UID brace-region form (FQN).</param>
    /// <param name="displayArg">The type-arg display form (short).</param>
    /// <param name="index">Zero-based position in the parent list.</param>
    /// <param name="count">Total count of args in the parent list.</param>
    /// <param name="internalUids">Classification set.</param>
    private static void AppendArgWithSeparator(StringBuilder sb, string uidArg, string displayArg, int index, int count, HashSet<string> internalUids)
    {
        AppendSpecArg(sb, uidArg, displayArg, internalUids);
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
        var href = ResolveHref(UidNormalization.ToOpenGenericUid(uid), UidNormalization.StripPrefix(uid), isInternal);

        // Docfx convention: spec.csharp uid is bare (no T: prefix).
        sb.Append("  - uid: ").AppendScalar(UidNormalization.StripPrefix(uid)).AppendLine()
            .Append("    name: ").AppendScalar(name).AppendLine();

        // spec.csharp components are always referenced as a separate
        // entry -- docfx emits `isExternal: true` regardless of whether
        // the target type is in the current walk. This keeps the spec
        // shape stable across pages and matches the docfx output we
        // diff against.
        sb.AppendLine("    isExternal: true");
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

    /// <summary>Writes <c>href: ...</c> at the requested indent when the value is non-empty.</summary>
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
    /// Renders one type-arg of a constructed generic -- recurses into
    /// nested generics so <c>Func&lt;IObservable&lt;int>></c>
    /// produces a nested spec list rather than a flat string. The
    /// UID brace region carries fully-qualified names so it drives
    /// component UIDs; the display brace region drives the labels.
    /// </summary>
    /// <param name="sb">Destination builder.</param>
    /// <param name="uidArg">FQN form from the UID brace region.</param>
    /// <param name="displayArg">Short form from the display brace region.</param>
    /// <param name="internalUids">Classification set.</param>
    private static void AppendSpecArg(StringBuilder sb, string uidArg, string displayArg, HashSet<string> internalUids)
    {
        var trimmedUid = uidArg.Trim();
        var trimmedDisplay = displayArg.Trim();
        var displayLt = trimmedDisplay.IndexOf('<', StringComparison.Ordinal);
        var uidBrace = trimmedUid.IndexOf('{', StringComparison.Ordinal);
        if (displayLt < 0 || uidBrace < 0)
        {
            // Plain leaf type: emit a single component using the FQN
            // from the UID side and the short label from the display
            // side. Display may be unqualified -- fall back to the
            // promoted UID name when display is empty.
            var leafUid = trimmedUid is [_, ..]
                ? "T:" + BclTypeAliases.ToClr(trimmedUid)
                : "T:" + BclTypeAliases.ToClr(trimmedDisplay);
            var label = trimmedDisplay is [_, ..] ? trimmedDisplay : trimmedUid;
            AppendSpecComponent(sb, leafUid, label, internalUids);
            return;
        }

        var displayBase = trimmedDisplay[..displayLt];
        var displayRegion = trimmedDisplay[(displayLt + 1)..^1];
        var uidBaseFqn = trimmedUid[..uidBrace];
        var uidRegion = trimmedUid[(uidBrace + 1)..^1];
        var arity = UidNormalization.CountTopLevelArgs(uidRegion, '{', '}');
        var openUid = "T:" + uidBaseFqn + "`" + arity.ToString(CultureInfo.InvariantCulture);
        AppendSpecComponent(sb, openUid, displayBase, internalUids);
        sb.AppendLine("  - name: <");
        var nestedDisplayArgs = UidNormalization.SplitTopLevelArgs(displayRegion, '<', '>');
        var nestedUidArgs = UidNormalization.SplitTopLevelArgs(uidRegion, '{', '}');
        var nestedCount = Math.Max(nestedDisplayArgs.Count, nestedUidArgs.Count);
        for (var i = 0; i < nestedCount; i++)
        {
            var nestedDisplay = i < nestedDisplayArgs.Count ? nestedDisplayArgs[i] : string.Empty;
            var nestedUid = i < nestedUidArgs.Count ? nestedUidArgs[i] : string.Empty;
            AppendArgWithSeparator(sb, nestedUid, nestedDisplay, i, nestedCount, internalUids);
        }

        sb.AppendLine("  - name: '>'");
    }
}
