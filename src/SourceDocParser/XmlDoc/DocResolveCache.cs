// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

namespace SourceDocParser;

/// <summary>
/// Per-resolver cache of <see cref="ApiDocumentation"/> instances keyed by <see cref="ISymbol"/>.
/// </summary>
/// <remarks>
/// This cache is created once per <see cref="DocResolver"/> instance and is not thread-safe.
/// The outer metadata extraction pipeline still runs assemblies in parallel, but each resolver
/// owns its own cache for the lifetime of a single walk/resolution scope.
/// </remarks>
internal sealed class DocResolveCache
{
    /// <summary>The backing dictionary for memoized documentation.</summary>
    private readonly Dictionary<ISymbol, ApiDocumentation> _bySymbol =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Gets the cached documentation for <paramref name="symbol"/>, creating and storing it on a cache miss.
    /// </summary>
    /// <typeparam name="TState">Caller-supplied state threaded into <paramref name="builder"/>.</typeparam>
    /// <param name="symbol">Symbol whose documentation is being resolved.</param>
    /// <param name="state">Additional state needed to build the value.</param>
    /// <param name="builder">Factory invoked once on a cache miss.</param>
    /// <returns>The cached or newly-created documentation.</returns>
    public ApiDocumentation GetOrAdd<TState>(
        ISymbol symbol,
        TState state,
        Func<ISymbol, TState, ApiDocumentation> builder)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_bySymbol, symbol, out var exists);
        if (!exists)
        {
            slot = builder(symbol, state);
        }

        return slot!;
    }
}
