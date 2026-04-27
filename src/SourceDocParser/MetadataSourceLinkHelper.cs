// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.SourceLink;

namespace SourceDocParser;

/// <summary>
/// Helper methods for Metadata Extractor source link collection.
/// </summary>
internal static class MetadataSourceLinkHelper
{
    /// <summary>
    /// Pulls every non-null source URL (type and member level) out of
    /// the merged catalog so the optional validator can be handed a
    /// flat list without re-walking the catalog.
    /// </summary>
    /// <param name="merged">Merged canonical types.</param>
    /// <returns>One entry per documented source URL.</returns>
    public static SourceLinkEntry[] CollectSourceLinks(ApiType[] merged)
    {
        // Most types contribute 0–1 source URLs (the type-level URL,
        // occasionally a member URL on top), so the type count is the
        // right capacity hint — leaves room for the dominant case
        // without front-loading dead capacity on a large catalog.
        var entries = new List<SourceLinkEntry>(merged.Length);

        for (var t = 0; t < merged.Length; t++)
        {
            var type = merged[t];
            if (type.SourceUrl is { Length: > 0 } typeUrl)
            {
                entries.Add(new(type.Uid, typeUrl));
            }

            var members = type switch
            {
                ApiObjectType o => o.Members,
                ApiUnionType u => u.Members,
                _ => null,
            };

            if (members is null)
            {
                continue;
            }

            for (var m = 0; m < members.Length; m++)
            {
                var member = members[m];
                if (member.SourceUrl is { Length: > 0 } memberUrl)
                {
                    entries.Add(new(member.Uid, memberUrl));
                }
            }
        }

        return [.. entries];
    }
}
