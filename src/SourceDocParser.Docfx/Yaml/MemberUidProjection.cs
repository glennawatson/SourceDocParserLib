// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Common;
using SourceDocParser.Model;

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Filters and projects an <see cref="ApiMember"/> array into the
/// alphabetically-sorted UID list docfx renders under
/// <c>children:</c>. Lifted out of <see cref="DocfxYamlBuilderExtensions"/>
/// so the count + collect + sort pipeline reads at problem-domain
/// level and is testable without going through a full page render.
/// </summary>
internal static class MemberUidProjection
{
    /// <summary>
    /// Counts non-compiler-generated members so an exact-size buffer
    /// can be allocated for the children list.
    /// </summary>
    /// <param name="members">Member array to scan.</param>
    /// <returns>The number of members that survive the compiler-gen filter.</returns>
    public static int CountKept(ApiMember[] members)
    {
        var kept = 0;
        for (var i = 0; i < members.Length; i++)
        {
            if (!CompilerGeneratedNames.IsCompilerGenerated(members[i].Name))
            {
                kept++;
            }
        }

        return kept;
    }

    /// <summary>
    /// Builds the alphabetically-sorted bare-uid array (M:/T:/etc.
    /// prefix already stripped) for the docfx <c>children:</c> field.
    /// One heap allocation, in-place sort. Returns an empty array
    /// when nothing survives the compiler-gen filter.
    /// </summary>
    /// <param name="members">Member array to project.</param>
    /// <returns>The alphabetically-sorted child UIDs.</returns>
    public static string[] CollectSortedChildUids(ApiMember[] members)
    {
        var kept = CountKept(members);
        if (kept is 0)
        {
            return [];
        }

        var uids = new string[kept];
        var cursor = 0;
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (CompilerGeneratedNames.IsCompilerGenerated(member.Name))
            {
                continue;
            }

            uids[cursor++] = CommentIdPrefix.Strip(member.Uid);
        }

        Array.Sort(uids, StringComparer.Ordinal);
        return uids;
    }
}
