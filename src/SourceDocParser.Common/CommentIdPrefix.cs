// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace SourceDocParser.Common;

/// <summary>
/// Strips the two-character Roslyn documentation-comment prefix
/// (<c>T:</c>, <c>M:</c>, <c>P:</c>, <c>E:</c>, <c>F:</c>, <c>N:</c>)
/// from a documentation comment ID. Used by both emitter packages —
/// docfx for its bare <c>uid</c> field, Zensical for stripped autoref
/// labels and namespace-prefix attribute filtering.
/// </summary>
public static class CommentIdPrefix
{
    /// <summary>Length of the standard Roslyn documentation-comment prefix (e.g. "T:").</summary>
    private const int PrefixLength = 2;

    /// <summary>Separator character between the type/member marker and the name.</summary>
    private const char Separator = ':';

    /// <summary>Returns <paramref name="commentId"/> with any leading Roslyn prefix removed.</summary>
    /// <param name="commentId">The prefixed comment ID (e.g. <c>T:Foo.Bar</c>).</param>
    /// <returns>The bare name (e.g. <c>Foo.Bar</c>); the original string when no prefix is present.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Strip(string commentId) =>
        commentId is [_, Separator, ..] ? commentId[PrefixLength..] : commentId;

    /// <summary>Span-returning variant — avoids the substring allocation when only a read is needed.</summary>
    /// <param name="commentId">The prefixed comment ID.</param>
    /// <returns>A span over the bare name region.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> StripSpan(string commentId) =>
        commentId is [_, Separator, ..] ? commentId.AsSpan(PrefixLength) : commentId.AsSpan();
}
