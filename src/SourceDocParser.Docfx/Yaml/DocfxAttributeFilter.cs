// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Docfx;

/// <summary>
/// Decides which attributes survive into a docfx ManagedReference
/// page's <c>attributes:</c> block. Mirrors docfx's own
/// <c>defaultfilterconfig.yml</c> rule for
/// <c>System.Runtime.CompilerServices</c>: drop everything in that
/// namespace except <c>ExtensionAttribute</c>. Walker emission is
/// faithful; this filter applies at the presentation layer.
/// </summary>
internal static class DocfxAttributeFilter
{
    /// <summary>Namespace prefixes whose attributes are dropped.</summary>
    private static readonly string[] _excludedNamespacePrefixes =
    [
        "System.Runtime.CompilerServices",
    ];

    /// <summary>UIDs explicitly allowed even when they fall under a denylisted namespace.</summary>
    private static readonly string[] _allowlistedUids =
    [
        "T:System.Runtime.CompilerServices.ExtensionAttribute",
    ];

    /// <summary>
    /// Returns the subset of <paramref name="attributes"/> that should
    /// reach the docfx YAML page; the rest are compiler-emitted noise
    /// (NullableContext, IsReadOnly, RefSafetyRules, etc.).
    /// </summary>
    /// <param name="attributes">The full list as exposed by the walker.</param>
    /// <returns>The presentation-filtered list, in original order.</returns>
    public static ApiAttribute[] Filter(ApiAttribute[] attributes)
    {
        if (attributes is [])
        {
            return [];
        }

        List<ApiAttribute> kept = new(attributes.Length);
        for (var i = 0; i < attributes.Length; i++)
        {
            if (!IsExcluded(attributes[i]))
            {
                kept.Add(attributes[i]);
            }
        }

        return kept.Count == attributes.Length ? attributes : [.. kept];
    }

    /// <summary>Tests whether an attribute matches the namespace denylist.</summary>
    /// <param name="attribute">The attribute to test.</param>
    /// <returns>True when the attribute should be dropped.</returns>
    private static bool IsExcluded(ApiAttribute attribute)
    {
        if (IsAllowlisted(attribute.Uid))
        {
            return false;
        }

        var bareName = attribute.Uid is [_, ':', ..] ? attribute.Uid.AsSpan(2) : attribute.Uid.AsSpan();
        for (var i = 0; i < _excludedNamespacePrefixes.Length; i++)
        {
            var prefix = _excludedNamespacePrefixes[i];
            if (bareName.Length > prefix.Length
                && bareName.StartsWith(prefix, StringComparison.Ordinal)
                && bareName[prefix.Length] == '.')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tests whether a UID appears on the allowlist.</summary>
    /// <param name="uid">Attribute type's documentation comment ID.</param>
    /// <returns>True when explicitly allowed despite namespace denylist matches.</returns>
    private static bool IsAllowlisted(string uid)
    {
        for (var i = 0; i < _allowlistedUids.Length; i++)
        {
            if (string.Equals(_allowlistedUids[i], uid, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
