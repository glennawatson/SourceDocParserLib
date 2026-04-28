// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Lightweight helper that produces docfx <c>commentId</c> values from
/// our own model -- kept separate so the YAML emitter stays focused on
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
}
