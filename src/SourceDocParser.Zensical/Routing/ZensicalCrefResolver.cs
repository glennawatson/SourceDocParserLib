// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using SourceDocParser.Zensical.Options;

namespace SourceDocParser.Zensical.Routing;

/// <summary>
/// <see cref="ICrefResolver"/> implementation tuned for the Zensical
/// (mkdocs Material + autorefs) output. Decides per UID whether the
/// reference renders as:
/// <list type="bullet">
/// <item><description>
/// an mkdocs-autorefs link (<c>[Name][UID]</c>) when the UID is in
/// the emitted set,
/// </description></item>
/// <item><description>
/// a Microsoft Learn external link when the UID names a BCL type
/// that we do not walk,
/// </description></item>
/// <item><description>
/// or inline code (<c>`Name`</c>) when neither applies -- empty UIDs
/// and the <c>!:</c> generic-parameter sentinel always fall here so
/// we never emit a reference link that mkdocs cannot resolve.
/// </description></item>
/// </list>
/// </summary>
public sealed class ZensicalCrefResolver : ICrefResolver
{
    /// <summary>The two namespace prefixes that map to Microsoft Learn rather than autorefs.</summary>
    private static readonly string[] _bclNamespacePrefixes = ["System", "Microsoft"];

    /// <summary>UIDs the Zensical emitter is producing pages for; resolved as autoref.</summary>
    private readonly FrozenSet<string> _emittedUids;

    /// <summary>Microsoft Learn base URL used for BCL types not in <see cref="_emittedUids"/>.</summary>
    private readonly string _microsoftLearnBaseUrl;

    /// <summary>Initializes a new instance of the <see cref="ZensicalCrefResolver"/> class.</summary>
    /// <param name="emittedUids">UIDs (types and members) the emitter is producing pages for. References that hit this set render as autoref links.</param>
    /// <param name="options">Emitter options -- contributes the Microsoft Learn base URL.</param>
    public ZensicalCrefResolver(FrozenSet<string> emittedUids, ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(emittedUids);
        ArgumentNullException.ThrowIfNull(options);
        _emittedUids = emittedUids;
        _microsoftLearnBaseUrl = options.MicrosoftLearnBaseUrl;
    }

    /// <inheritdoc />
    public string Render(string uid, ReadOnlySpan<char> shortName)
    {
        if (uid is null or [])
        {
            return $"`{shortName}`";
        }

        // Generic-parameter sentinel -- Roslyn emits `!:T`, `!:TAwaiter`,
        // etc. when a cref points at a method-local type parameter.
        // Always inline code; an autoref link could never resolve.
        if (uid is ['!', ':', ..])
        {
            return $"`{shortName}`";
        }

        // In-set wins over BCL prefix detection: types we walk (e.g.
        // System.Reactive.Unit) start with "System." but live on a
        // page in our own site, not on Microsoft Learn. The autoref
        // bracket carries the autoref-safe form (backtick -> hyphen)
        // so Python-Markdown's reference-label parser doesn't trip on
        // arity backticks; the matching anchor on the target page
        // applies the same transform via PageFrontmatter.
        var canonicalUid = UidNormaliser.Normalise(uid);
        if (_emittedUids.Contains(canonicalUid))
        {
            return $"[{shortName}][{UidNormaliser.ToAutorefId(canonicalUid)}]";
        }

        if (TryFormatAsMicrosoftLearn(canonicalUid, shortName, out var learnLink))
        {
            return learnLink;
        }

        // Unknown UID and not BCL -- emit inline code so mkdocs-autorefs
        // doesn't warn about an unresolvable reference target.
        return $"`{shortName}`";
    }

    /// <summary>Returns true when <paramref name="bareName"/> starts with one of the BCL namespace prefixes.</summary>
    /// <param name="bareName">Type name without the commentId prefix.</param>
    /// <returns>True when the name belongs to <c>System.*</c> or <c>Microsoft.*</c>.</returns>
    private static bool StartsWithBclPrefix(ReadOnlySpan<char> bareName)
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
    /// Composes <c>[display](baseUrl + slug)</c> in a single string
    /// allocation. Slug = bareName lowercased with arity backticks
    /// replaced by hyphens (Microsoft Learn URL convention -- e.g.
    /// <c>System.Action`1</c> becomes <c>system.action-1</c>).
    /// </summary>
    /// <param name="displayName">Link text.</param>
    /// <param name="baseUrl">Microsoft Learn URL prefix.</param>
    /// <param name="bareName">UID without the commentId prefix.</param>
    /// <returns>The Markdown link.</returns>
    [SuppressMessage("Minor Code Smell", "S4040:Strings should be normalized to uppercase", Justification = "Microsoft Learn URLs are case-sensitive.")]
    private static string ComposeLearnLink(ReadOnlySpan<char> displayName, string baseUrl, ReadOnlySpan<char> bareName)
    {
        var totalLen = 1 + displayName.Length + 2 + baseUrl.Length + bareName.Length + 1;
        Span<char> dest = totalLen <= 512 ? stackalloc char[totalLen] : new char[totalLen];
        var pos = 0;
        dest[pos++] = '[';
        displayName.CopyTo(dest[pos..]);
        pos += displayName.Length;
        dest[pos++] = ']';
        dest[pos++] = '(';
        baseUrl.AsSpan().CopyTo(dest[pos..]);
        pos += baseUrl.Length;
        for (var i = 0; i < bareName.Length; i++)
        {
            var c = bareName[i];
            dest[pos++] = c == '`' ? '-' : char.ToLowerInvariant(c);
        }

        dest[pos] = ')';
        return new(dest);
    }

    /// <summary>
    /// Tries to render <paramref name="canonicalUid"/> as a Microsoft
    /// Learn link. Returns false when the UID doesn't belong to a
    /// recognised BCL namespace; caller falls back to inline code.
    /// </summary>
    /// <param name="canonicalUid">Normalised commentId UID.</param>
    /// <param name="displayName">Human-readable name to use as link text.</param>
    /// <param name="link">The rendered Markdown link, when the UID matches.</param>
    /// <returns>True when a Microsoft Learn link was produced.</returns>
    private bool TryFormatAsMicrosoftLearn(
        string canonicalUid,
        ReadOnlySpan<char> displayName,
        out string link)
    {
        link = string.Empty;
        var bareName = canonicalUid is [_, ':', ..] ? canonicalUid.AsSpan()[2..] : canonicalUid.AsSpan();
        if (!StartsWithBclPrefix(bareName))
        {
            return false;
        }

        link = ComposeLearnLink(displayName, _microsoftLearnBaseUrl, bareName);
        return true;
    }
}
