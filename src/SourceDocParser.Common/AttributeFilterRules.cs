// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Common;

/// <summary>
/// Shared denylist + allowlist that decides which attributes survive
/// into rendered output. Mirrors docfx's <c>defaultfilterconfig.yml</c>
/// rule for <c>System.Runtime.CompilerServices</c>: drop the
/// compiler-emitted markers (NullableContext, IsReadOnly,
/// RefSafetyRules, etc.) but keep <c>ExtensionAttribute</c> visible.
/// Walker emission is faithful — this filter applies at the
/// presentation layer so every emitter can opt in to the same rule
/// set with one call.
/// </summary>
public static class AttributeFilterRules
{
    /// <summary>Namespace prefixes whose attributes are dropped from rendered output.</summary>
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
    /// Tests whether an attribute identified by its documentation
    /// comment ID should be excluded from rendered output.
    /// </summary>
    /// <param name="attributeUid">The attribute type's documentation comment ID.</param>
    /// <returns>True when the attribute is on the namespace denylist and not on the allowlist.</returns>
    public static bool IsExcluded(string attributeUid)
    {
        if (IsAllowlisted(attributeUid))
        {
            return false;
        }

        var bareName = CommentIdPrefix.StripSpan(attributeUid);
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

    /// <summary>Tests whether <paramref name="attributeUid"/> is on the explicit allowlist.</summary>
    /// <param name="attributeUid">Attribute type's documentation comment ID.</param>
    /// <returns>True when the attribute is allowed regardless of namespace denylist matches.</returns>
    private static bool IsAllowlisted(string attributeUid)
    {
        for (var i = 0; i < _allowlistedUids.Length; i++)
        {
            if (string.Equals(_allowlistedUids[i], attributeUid, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
