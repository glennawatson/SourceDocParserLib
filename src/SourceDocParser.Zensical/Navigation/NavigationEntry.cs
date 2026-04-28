// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Navigation;

/// <summary>
/// One leaf in the nav graph -- a single type page.
/// </summary>
/// <param name="Title">
/// Pre-formatted display name with generic placeholders
/// (<c>Change&lt;T&gt;</c>, <c>Change&lt;T1, T2&gt;</c>) -- safe to
/// render verbatim in any sidebar.
/// </param>
/// <param name="Path">Relative page path with forward slashes.</param>
/// <param name="Kind">
/// Coarse type-kind classification. Consumers append it as a
/// Microsoft-Learn-style suffix (<c>Class</c> / <c>Struct</c> /
/// <c>Delegate</c>) so generic-arity siblings don't read as duplicate
/// sidebar entries.
/// </param>
/// <param name="Arity">
/// Number of generic type parameters declared on the type itself.
/// Zero for non-generic types. Surfaced separately from
/// <paramref name="Title"/> so consumers can render their own naming
/// scheme (e.g. <c>"Change`2"</c>) without re-parsing the formatted
/// title.
/// </param>
/// <param name="TypeParameters">
/// The author-given generic type parameter names (e.g.
/// <c>["TKey", "TValue"]</c> for <c>Dictionary&lt;TKey, TValue&gt;</c>).
/// Empty for non-generic types and for any type whose declaring
/// metadata didn't expose names. Lets consumers render
/// <c>"Change&lt;TKey, TValue&gt; Class"</c> instead of the
/// placeholder-only <c>"Change&lt;T1, T2&gt;"</c> when they want
/// MS-Learn-style readability.
/// </param>
public readonly record struct NavigationEntry(
    string Title,
    string Path,
    NavigationTypeKind Kind,
    int Arity,
    string[] TypeParameters);
