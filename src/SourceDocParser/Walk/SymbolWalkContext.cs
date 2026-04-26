// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser;

/// <summary>
/// Per-walk state bundle threaded through every <see cref="SymbolWalker"/>
/// helper. Bundling the per-walk caches + dependencies into a single
/// record means every internal helper takes one parameter instead of
/// five-to-seven, and the cache lifetimes are explicit (one context per
/// <c>Walk</c> call).
/// </summary>
/// <param name="AssemblyName">Simple name of the declaring assembly.</param>
/// <param name="Tfm">TFM the declaring assembly was loaded under.</param>
/// <param name="Docs">XML doc resolver memoising per-symbol parses for the duration of the walk.</param>
/// <param name="TypeRefs">Per-walk cache of <see cref="ApiTypeReference"/> records keyed by Roslyn type symbol.</param>
/// <param name="SourceLinks">SourceLink resolver scoped to the assembly.</param>
/// <param name="NamespaceDisplayNames">Per-walk namespace-display cache. Not thread-safe; owned by a single <c>Walk</c> call.</param>
/// <param name="AppliesTo">Shared single-element list <c>[Tfm]</c> attached to every <see cref="ApiType"/> emitted by this walk; one allocation per walk instead of per type.</param>
internal sealed record SymbolWalkContext(
    string AssemblyName,
    string Tfm,
    IDocResolver Docs,
    TypeReferenceCache TypeRefs,
    ISourceLinkResolver SourceLinks,
    NamespaceDisplayNameCache NamespaceDisplayNames,
    List<string> AppliesTo);
