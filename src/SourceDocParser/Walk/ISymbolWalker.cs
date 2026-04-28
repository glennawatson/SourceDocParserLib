// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using SourceDocParser.Model;
using SourceDocParser.SourceLink;

namespace SourceDocParser.Walk;

/// <summary>
/// Walks an <see cref="IAssemblySymbol"/> public surface into an
/// <see cref="ApiCatalog"/>. Implementations are expected to be stateless
/// across calls so the same walker can be reused across many assemblies.
/// </summary>
public interface ISymbolWalker
{
    /// <summary>
    /// Walks the public types of <paramref name="assembly"/> and returns a
    /// catalog tagged with <paramref name="tfm"/>.
    /// </summary>
    /// <param name="tfm">TFM the assembly was extracted from; recorded on the catalog.</param>
    /// <param name="assembly">Assembly symbol to walk.</param>
    /// <param name="compilation">Compilation that produced <paramref name="assembly"/>; passed through to the doc resolver for cref lookups.</param>
    /// <param name="sourceLinks">Resolver scoped to the assembly; populates source URLs when SourceLink data is available.</param>
    /// <returns>The generated API catalog.</returns>
    ApiCatalog Walk(string tfm, IAssemblySymbol assembly, Compilation compilation, ISourceLinkResolver sourceLinks);
}
