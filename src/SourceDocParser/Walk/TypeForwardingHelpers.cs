// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SourceDocParser.Walk;

/// <summary>
/// Small composable helpers for walking type-forwarding metadata on
/// an <see cref="IAssemblySymbol"/>. Roslyn surfaces forwards via
/// <see cref="IAssemblySymbol.GetForwardedTypes"/> -- the targets
/// resolve to their real definitions when the destination assembly
/// is in the compilation references, or to error symbols when it
/// isn't. Each helper here does one thing so the SymbolWalker can
/// compose them into its existing iteration shape and the tests can
/// pin each filter in isolation.
/// </summary>
internal static class TypeForwardingHelpers
{
    /// <summary>
    /// Returns the raw forwarded-type array. Thin pass-through over
    /// <see cref="IAssemblySymbol.GetForwardedTypes"/> so callers don't
    /// have to repeat the null/default handling shape.
    /// </summary>
    /// <param name="assembly">Assembly whose forward attributes to read.</param>
    /// <returns>The forwarded type array -- possibly empty, never default.</returns>
    public static ImmutableArray<INamedTypeSymbol> GetForwardedTypes(IAssemblySymbol assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var forwarded = assembly.GetForwardedTypes();
        return forwarded.IsDefault ? [] : forwarded;
    }

    /// <summary>
    /// Returns true when <paramref name="forwarded"/> resolves to a
    /// real type definition -- i.e. the destination assembly is loaded
    /// and Roslyn handed back something other than an error symbol. A
    /// false return means the metadata says "this type lives in
    /// assembly X" but X isn't in the compilation references; the
    /// best-effort path is to skip it (a follow-up that walks
    /// transitive package deps will resolve it later).
    /// </summary>
    /// <param name="forwarded">Forwarded target symbol.</param>
    /// <returns>True when the target resolves to a non-error definition.</returns>
    public static bool IsResolvable(INamedTypeSymbol forwarded)
    {
        ArgumentNullException.ThrowIfNull(forwarded);
        return forwarded.TypeKind != TypeKind.Error;
    }

    /// <summary>
    /// Returns true when <paramref name="forwarded"/> would already be
    /// represented in the catalog by its UID. Callers maintain the
    /// hash set as they walk the namespace tree and pass it in here
    /// to avoid emitting duplicate entries when an umbrella assembly
    /// forwards a type that another walked sibling already produced.
    /// </summary>
    /// <param name="forwarded">Forwarded type to check.</param>
    /// <param name="seenTypeUids">UIDs already collected by the walker.</param>
    /// <returns>True when the type is already represented.</returns>
    public static bool IsAlreadyCollected(INamedTypeSymbol forwarded, HashSet<string> seenTypeUids)
    {
        ArgumentNullException.ThrowIfNull(forwarded);
        ArgumentNullException.ThrowIfNull(seenTypeUids);
        var uid = forwarded.GetDocumentationCommentId();
        return uid is { Length: > 0 } && seenTypeUids.Contains(uid);
    }

    /// <summary>
    /// Pushes every forwarded type onto <paramref name="pending"/>.
    /// Lets the SymbolWalker reuse its existing
    /// <see cref="Stack{T}"/> instead of allocating a new one per
    /// assembly walk. Caller is responsible for filtering as items
    /// pop -- this overload is the cheapest seed.
    /// </summary>
    /// <param name="assembly">Assembly whose forwards to seed from.</param>
    /// <param name="pending">Pre-allocated stack to push into.</param>
    /// <returns>The number of types pushed.</returns>
    public static int SeedPending(IAssemblySymbol assembly, Stack<INamedTypeSymbol> pending)
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

    /// <summary>
    /// Pushes every nested type of <paramref name="parent"/> onto
    /// <paramref name="pending"/> so the SymbolWalker's main loop
    /// surfaces them for visibility filtering on the next pop.
    /// Mirrors the namespace-walk shape; kept here so the forwarding
    /// path stays a one-line composition with the seed call.
    /// </summary>
    /// <param name="parent">Type whose nested types to enqueue.</param>
    /// <param name="pending">Pre-allocated stack to push into.</param>
    /// <returns>The number of nested types pushed.</returns>
    public static int PushNested(INamedTypeSymbol parent, Stack<INamedTypeSymbol> pending)
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
