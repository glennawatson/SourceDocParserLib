// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser;

/// <summary>
/// Per-walk cache of namespace display names keyed by <see cref="INamespaceSymbol"/>.
/// </summary>
/// <remarks>
/// This cache belongs to a single <see cref="SymbolWalker.Walk"/> invocation and is not thread-safe.
/// The walker processes many assemblies in parallel at a higher level, but each walk builds and owns
/// its own namespace cache so repeated <c>ToDisplayString()</c> calls stay allocation-light.
/// </remarks>
internal sealed class NamespaceDisplayNameCache
{
    /// <summary>The backing dictionary for formatted namespace names.</summary>
    private readonly Dictionary<INamespaceSymbol, string> _byNamespace =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Gets the cached display name for <paramref name="ns"/>, formatting and caching it on a miss.
    /// </summary>
    /// <param name="ns">Namespace symbol to render.</param>
    /// <returns>The formatted display name.</returns>
    public string GetOrAdd(INamespaceSymbol ns)
    {
        if (_byNamespace.TryGetValue(ns, out var cached))
        {
            return cached;
        }

        var formatted = ns.ToDisplayString();
        _byNamespace[ns] = formatted;
        return formatted;
    }
}
