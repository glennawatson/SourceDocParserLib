// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Backwards-compatible <see cref="ICrefResolver"/> that emits the
/// pre-refactor "always autoref" form: <c>[shortName][uid]</c>. Used
/// when the converter is invoked without a custom resolver — keeps the
/// behaviour callers had before <see cref="ICrefResolver"/> existed,
/// so unit tests and ad-hoc converter use don't need to wire one up.
/// </summary>
/// <remarks>
/// Generic-parameter placeholder UIDs (the <c>!:T</c> form Roslyn
/// emits when a cref points at a method-local type parameter) get
/// rendered as inline code instead — autoref form for those would
/// always fail to resolve and the inline-code form matches what
/// every emitter wants in practice.
/// </remarks>
public sealed class DefaultCrefResolver : ICrefResolver
{
    /// <summary>The shared singleton instance.</summary>
    public static readonly DefaultCrefResolver Instance = new();

    /// <inheritdoc />
    public string Render(string uid, ReadOnlySpan<char> shortName)
    {
        if (uid is null or [])
        {
            return $"`{shortName}`";
        }

        if (uid is ['!', ':', ..])
        {
            return $"`{shortName}`";
        }

        return $"[{shortName}][{uid}]";
    }
}
