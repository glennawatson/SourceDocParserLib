// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Resolves a documentation cross-reference (a Roslyn cref / commentId
/// like <c>T:System.String</c>, <c>M:Foo.Bar(System.Int32)</c>, or the
/// generic-parameter sentinel <c>!:T</c>) into the Markdown fragment
/// that should appear inline at the reference site.
/// </summary>
/// <remarks>
/// <para>
/// The resolver decides what the Markdown should look like — a
/// cross-reference link to a page in the same site (mkdocs-autorefs
/// form), an external link (e.g. Microsoft Learn for the BCL),
/// inline code for unresolvable references, or any other shape an
/// emitter wants to produce. The XmlDoc converter and the Zensical /
/// Docfx routers all delegate through this single seam so the rules
/// for "what does a reference look like in this output" live in one
/// place per emitter.
/// </para>
/// <para>
/// Implementations must be thread-safe across <see cref="Render"/>
/// calls — emitters that parallelise page rendering will share a
/// single resolver instance across worker tasks.
/// </para>
/// </remarks>
public interface ICrefResolver
{
    /// <summary>
    /// Renders <paramref name="uid"/> as a Markdown fragment, with
    /// <paramref name="shortName"/> as the suggested display text.
    /// </summary>
    /// <param name="uid">The cref / commentId UID being referenced.</param>
    /// <param name="shortName">A short, human-readable display name. Resolvers may use it as link text or ignore it and produce their own.</param>
    /// <returns>The Markdown fragment to splice in at the reference site.</returns>
    string Render(string uid, ReadOnlySpan<char> shortName);
}
