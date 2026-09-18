// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SourceDocParser.Walk;

/// <summary>Inspects forwarded and nested type metadata for symbol resolution.</summary>
internal static class TypeForwardingHelpers
{
    /// <summary>
    /// Returns the raw forwarded-type array. Thin pass-through over
    /// <see cref="IAssemblySymbol.GetForwardedTypes"/> so callers don't
    /// have to repeat the null/default handling shape.
    /// </summary>
    /// <param name="assembly">Assembly whose forward attributes to read.</param>
    /// <returns>The forwarded type array -- possibly empty, never default.</returns>
    internal static ImmutableArray<INamedTypeSymbol> GetForwardedTypes(IAssemblySymbol assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var forwarded = assembly.GetForwardedTypes();
        return forwarded.IsDefault ? [] : forwarded;
    }

    /// <summary>Returns whether a type has a resolved definition.</summary>
    /// <param name="forwarded">Forwarded target symbol.</param>
    /// <returns>True when the target resolves to a non-error definition.</returns>
    internal static bool IsResolvable(INamedTypeSymbol forwarded)
    {
        ArgumentNullException.ThrowIfNull(forwarded);
        return forwarded.TypeKind != TypeKind.Error;
    }

    /// <summary>Returns whether a type's UID is represented in the catalog.</summary>
    /// <param name="forwarded">Forwarded type to check.</param>
    /// <param name="seenTypeUids">UIDs already collected by the walker.</param>
    /// <returns>True when the type is already represented.</returns>
    internal static bool IsAlreadyCollected(INamedTypeSymbol forwarded, HashSet<string> seenTypeUids)
    {
        ArgumentNullException.ThrowIfNull(forwarded);
        ArgumentNullException.ThrowIfNull(seenTypeUids);
        var uid = forwarded.GetDocumentationCommentId();
        return uid is { Length: > 0 } && seenTypeUids.Contains(uid);
    }

    /// <summary>Enqueues forwarded targets for metadata inspection.</summary>
    /// <param name="assembly">Assembly whose forwards to seed from.</param>
    /// <param name="pending">Pre-allocated stack to push into.</param>
    /// <returns>The number of types pushed.</returns>
    internal static int SeedPending(IAssemblySymbol assembly, Stack<INamedTypeSymbol> pending)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(pending);
        var forwarded = GetForwardedTypes(assembly);
        for (var i = 0; i < forwarded.Length; i++)
        {
            pending.Push(forwarded[i]);
        }

        return forwarded.Length;
    }

    /// <summary>Enqueues nested declared types for visibility checks.</summary>
    /// <param name="parent">Type whose nested types to enqueue.</param>
    /// <param name="pending">Pre-allocated stack to push into.</param>
    /// <returns>The number of nested types pushed.</returns>
    internal static int PushNested(INamedTypeSymbol parent, Stack<INamedTypeSymbol> pending)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(pending);
        var nested = parent.GetTypeMembers();
        for (var i = 0; i < nested.Length; i++)
        {
            pending.Push(nested[i]);
        }

        return nested.Length;
    }
}
