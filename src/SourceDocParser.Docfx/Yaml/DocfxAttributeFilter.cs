// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Common;
using SourceDocParser.Model;

namespace SourceDocParser.Docfx.Yaml;

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
    /// <summary>
    /// Returns the subset of <paramref name="attributes"/> that should
    /// reach the docfx YAML page; the rest are compiler-emitted noise
    /// (NullableContext, IsReadOnly, RefSafetyRules, etc.). The denylist
    /// + allowlist live in <see cref="AttributeFilterRules"/> so every
    /// emitter applies the same rule set.
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
            if (!AttributeFilterRules.IsExcluded(attributes[i].Uid))
            {
                kept.Add(attributes[i]);
            }
        }

        return kept.Count == attributes.Length ? attributes : [.. kept];
    }
}
