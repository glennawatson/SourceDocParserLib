// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

namespace SourceDocParser;

/// <summary>
/// Per-walk cache of <see cref="ApiTypeReference"/> instances keyed by <see cref="ITypeSymbol"/>.
/// </summary>
/// <remarks>
/// Caching formatted display names and UIDs avoids redundant allocations during assembly walks.
/// The cache is scoped to one <see cref="SymbolWalker.Walk"/> invocation, so lookups stay
/// single-threaded even though the outer metadata extraction pipeline processes assemblies in parallel.
/// </remarks>
internal sealed class TypeReferenceCache
{
    /// <summary>
    /// The backing dictionary for cached type references.
    /// </summary>
    private readonly Dictionary<ITypeSymbol, ApiTypeReference> _byType =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Gets or adds a cached reference for the specified type.
    /// </summary>
    /// <param name="type">The type symbol to look up.</param>
    /// <param name="builder">The factory used to create the reference on a cache miss.</param>
    /// <returns>A formatted <see cref="ApiTypeReference"/>.</returns>
    public ApiTypeReference GetOrAdd(ITypeSymbol type, Func<ITypeSymbol, ApiTypeReference> builder)
    {
        // GetValueRefOrAddDefault collapses TryGetValue + indexer-set
        // into a single hash lookup; the ref lets us write into the
        // dictionary's slot directly on a cache miss.
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_byType, type, out var exists);
        if (!exists)
        {
            slot = builder(type);
        }

        return slot!;
    }
}
