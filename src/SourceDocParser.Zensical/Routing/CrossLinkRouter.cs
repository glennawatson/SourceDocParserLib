// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using SourceDocParser.Model;
using SourceDocParser.Zensical.Options;

namespace SourceDocParser.Zensical.Routing;

/// <summary>
/// Decides how a type reference renders in Markdown. For types we
/// emit pages for (matched by a <see cref="PackageRoutingRule"/>),
/// produces an mkdocs-autorefs key the consuming Zensical site
/// resolves locally. For BCL types we don't walk, produces a
/// Microsoft Learn URL. Anything else falls back to inline code.
/// </summary>
internal static class CrossLinkRouter
{
    /// <summary>The two namespace prefixes that map to Microsoft Learn rather than autorefs.</summary>
    private static readonly string[] _bclNamespacePrefixes = ["System", "Microsoft"];

    /// <summary>
    /// Renders <paramref name="reference"/> as an autoref link
    /// when its UID points at a primary package's type, a
    /// Microsoft Learn link when it's BCL, or inline code as the
    /// final fallback.
    /// </summary>
    /// <param name="reference">Type reference to render.</param>
    /// <param name="options">Routing + Microsoft Learn URL base.</param>
    /// <returns>The Markdown fragment for the reference.</returns>
    public static string Format(ApiTypeReference reference, ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(options);

        if (reference.Uid is not [_, ..] uid)
        {
            return $"`{reference.DisplayName}`";
        }

        var canonicalUid = UidNormaliser.Normalise(uid);
        if (TryFormatAsMicrosoftLearn(canonicalUid, reference.DisplayName, options, out var learnLink))
        {
            return learnLink;
        }

        return $"[{reference.DisplayName}][{canonicalUid}]";
    }

    /// <summary>
    /// Tries to render <paramref name="canonicalUid"/> as a
    /// Microsoft Learn link. Returns false when the UID doesn't
    /// belong to a recognised BCL namespace; caller falls back to
    /// the autoref form.
    /// </summary>
    /// <param name="canonicalUid">Normalised commentId UID.</param>
    /// <param name="displayName">Human-readable name to use as link text.</param>
    /// <param name="options">Microsoft Learn base URL holder.</param>
    /// <param name="link">The rendered Markdown link, when the UID matches.</param>
    /// <returns>True when a Microsoft Learn link was produced.</returns>
    private static bool TryFormatAsMicrosoftLearn(
        string canonicalUid,
        string displayName,
        ZensicalEmitterOptions options,
        out string link)
    {
        link = string.Empty;
        var bareName = canonicalUid is [_, ':', ..] ? canonicalUid[2..] : canonicalUid;
        if (!StartsWithBclPrefix(bareName))
        {
            return false;
        }

        // Microsoft Learn URLs lowercase the type and replace the
        // arity backtick with a hyphen — System.Action`1 becomes
        // system.action-1.
        var slug = bareName.ToLower(CultureInfo.InvariantCulture).Replace('`', '-');
        link = $"[{displayName}]({options.MicrosoftLearnBaseUrl}{slug})";
        return true;
    }

    /// <summary>Returns true when <paramref name="bareName"/> starts with one of the BCL namespace prefixes.</summary>
    /// <param name="bareName">Type name without the commentId prefix.</param>
    /// <returns>True when the name belongs to <c>System.*</c> or <c>Microsoft.*</c>.</returns>
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
}
