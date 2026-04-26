// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Docfx;

/// <summary>
/// Lightweight helper that produces docfx <c>commentId</c> values from
/// our own model — kept separate so the YAML emitter stays focused on
/// layout and the convention lives in one place.
/// </summary>
internal static class DocfxCommentId
{
    /// <summary>Returns a <c>T:Namespace.Type</c> commentId for a type.</summary>
    /// <param name="type">Type to format.</param>
    /// <returns>The commentId string, or empty when the type has no UID.</returns>
    public static string ForType(ApiType type)
    {
        if (type.Uid is [_, ..])
        {
            return type.Uid;
        }

        return type.FullName is [_, ..] ? $"T:{type.FullName}" : string.Empty;
    }

    /// <summary>
    /// Returns the docfx commentId for a member. The walker already
    /// captures the Roslyn-style ID (e.g. <c>M:Foo.Bar.Baz(System.Int32)</c>),
    /// so we just pass it through; the prefix already encodes the kind.
    /// </summary>
    /// <param name="member">Member to format.</param>
    /// <returns>The commentId string, or empty when not present.</returns>
    public static string ForMember(ApiMember member) => member.Uid;

    /// <summary>
    /// Strips the two-character commentId prefix (<c>T:</c>, <c>M:</c>,
    /// <c>P:</c>, <c>E:</c>, <c>F:</c>, <c>N:</c>) from a Roslyn-style
    /// commentId so it can be written as a docfx <c>uid</c> field. Docfx
    /// keeps the prefix in <c>commentId</c> only; <c>uid</c>, plus every
    /// cross-reference (<c>parent</c>, <c>children</c>,
    /// <c>references[].uid</c>), uses the bare canonical name.
    /// </summary>
    /// <param name="commentId">The prefixed commentId (e.g. <c>T:Foo.Bar</c>).</param>
    /// <returns>The bare uid (e.g. <c>Foo.Bar</c>), or the original string when no prefix is present.</returns>
    public static string ToUid(string commentId)
    {
        if (commentId is [_, ':', ..])
        {
            return commentId[2..];
        }

        return commentId;
    }
}
