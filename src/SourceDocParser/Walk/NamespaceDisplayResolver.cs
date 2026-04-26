// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser;

/// <summary>
/// Resolves a Roslyn <see cref="INamespaceSymbol"/> to its display
/// string, going through the per-walk
/// <see cref="SymbolWalkContext.NamespaceDisplayNames"/> cache so
/// every type formats its containing namespace at most once. Without
/// this cache <c>ToDisplayString</c> dominates the per-type
/// allocation profile of the walk.
/// </summary>
internal static class NamespaceDisplayResolver
{
    /// <summary>
    /// Returns the cached display string for <paramref name="ns"/>,
    /// lazily populating the cache on the first encounter. The
    /// global namespace folds to the empty string so callers can
    /// branch on it via list patterns.
    /// </summary>
    /// <param name="context">Per-walk context owning the namespace cache.</param>
    /// <param name="ns">Namespace symbol to format; may be null.</param>
    /// <returns>The cached display string, or empty for the global namespace / null.</returns>
    internal static string Resolve(SymbolWalkContext context, INamespaceSymbol? ns)
    {
        if (ns is not { IsGlobalNamespace: false })
        {
            return string.Empty;
        }

        return context.NamespaceDisplayNames.GetOrAdd(ns);
    }
}
