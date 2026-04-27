// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Walk;

/// <summary>
/// Walks an <see cref="IAssemblySymbol"/>'s public surface to
/// produce an <see cref="ApiCatalog"/>. Stateless apart from the
/// per-walk <see cref="IDocResolver"/> that memoizes XML doc
/// parses. Iteration uses explicit stacks to avoid closure
/// allocations from recursion. Per-symbol extraction is delegated
/// to <see cref="TypeBuilder"/>, <see cref="MemberBuilder"/>,
/// <see cref="ExtensionBlockBuilder"/>, and
/// <see cref="NamespaceDisplayResolver"/> so each piece is unit-
/// testable in isolation against synthesised Roslyn symbols.
/// </summary>
public sealed class SymbolWalker : ISymbolWalker
{
    /// <summary>Factory invoked once per <see cref="Walk"/> to create the per-compilation doc resolver.</summary>
    private readonly Func<Compilation, IDocResolver> _docResolverFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SymbolWalker"/> class
    /// using the default doc-resolver factory.
    /// </summary>
    public SymbolWalker()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SymbolWalker"/> class.
    /// </summary>
    /// <param name="docResolverFactory">Factory invoked once per <see cref="Walk"/> to create the per-compilation doc resolver. Defaults to <c>c =&gt; new DocResolver(c)</c>.</param>
    public SymbolWalker(Func<Compilation, IDocResolver>? docResolverFactory) =>
        _docResolverFactory = docResolverFactory ?? (static c => new DocResolver(c));

    /// <summary>
    /// Walks through symbols in the provided compilation, resolving documentation and source links
    /// to construct an API catalog.
    /// </summary>
    /// <param name="tfm">The target framework moniker (TFM) of the assembly being analyzed.</param>
    /// <param name="assembly">The assembly symbol representing the assembly to be walked.</param>
    /// <param name="compilation">The compilation object containing the assembly's symbols and references.</param>
    /// <param name="sourceLinks">The resolver for source link mappings associated with the assembly.</param>
    /// <returns>An <see cref="ApiCatalog"/> representing the analyzed API and its associated metadata.</returns>
    public ApiCatalog Walk(string tfm, IAssemblySymbol assembly, Compilation compilation, ISourceLinkResolver sourceLinks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tfm);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(sourceLinks);
        return WalkCore(tfm, assembly, _docResolverFactory(compilation), sourceLinks);
    }

    /// <summary>
    /// Static implementation of <see cref="Walk"/>. Kept separate so the
    /// instance method is a one-liner and the body remains pure /
    /// shareable across walker instances without per-instance state.
    /// </summary>
    /// <param name="tfm">TFM the assembly was extracted from.</param>
    /// <param name="assembly">Assembly symbol to walk.</param>
    /// <param name="docs">Doc resolver scoped to the compilation that produced <paramref name="assembly"/>.</param>
    /// <param name="sourceLinks">Resolver scoped to <paramref name="assembly"/>.</param>
    /// <returns>The generated API catalog.</returns>
    internal static ApiCatalog WalkCore(string tfm, IAssemblySymbol assembly, IDocResolver docs, ISourceLinkResolver sourceLinks)
    {
        var context = new SymbolWalkContext(
            AssemblyName: assembly.Name,
            Tfm: tfm,
            Docs: docs,
            TypeRefs: new(),
            SourceLinks: sourceLinks,
            NamespaceDisplayNames: new(),
            AppliesTo: [tfm]);

        // Total count isn't known upfront — typical assemblies land in
        // the low hundreds of public types — so let the list grow
        // dynamically rather than picking an arbitrary capacity.
        List<ApiType> types = [];
        var seenTypeUids = new HashSet<string>(StringComparer.Ordinal);
        var pendingNamespaces = new Stack<INamespaceSymbol>();
        var pendingTypes = new Stack<INamedTypeSymbol>();

        pendingNamespaces.Push(assembly.GlobalNamespace);

        while (pendingNamespaces.TryPop(out var ns))
        {
            foreach (var namespaceMember in ns.GetNamespaceMembers())
            {
                pendingNamespaces.Push(namespaceMember);
            }

            var namespaceTypes = ns.GetTypeMembers();
            for (var i = 0; i < namespaceTypes.Length; i++)
            {
                pendingTypes.Push(namespaceTypes[i]);
            }

            DrainPendingTypes(pendingTypes, types, seenTypeUids, context);
        }

        // Type forwards: an umbrella assembly (e.g. Splat.dll) may
        // declare [TypeForwardedTo(typeof(Foo))] for types whose real
        // definition lives in a sibling assembly (Splat.Core.dll).
        // Seed the same pending stack so DrainPendingTypes surfaces
        // them through the existing visibility / dedupe filtering.
        TypeForwardingHelpers.SeedPending(assembly, pendingTypes);
        DrainPendingTypes(pendingTypes, types, seenTypeUids, context);

        types.Sort(static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return new(tfm, [.. types]);
    }

    /// <summary>
    /// Pops every type off <paramref name="pendingTypes"/>, runs the
    /// shared visibility / build-or-skip / nested-push pipeline, and
    /// records each successfully built type's UID in
    /// <paramref name="seenTypeUids"/> so a later forwarded-type pass
    /// can skip duplicates. Internal so tests can drive the drain
    /// loop directly with synthesised symbols.
    /// </summary>
    /// <param name="pendingTypes">Stack of types to drain.</param>
    /// <param name="types">Catalog list to append into.</param>
    /// <param name="seenTypeUids">UIDs already produced — used for dedupe.</param>
    /// <param name="context">Per-walk state bundle.</param>
    internal static void DrainPendingTypes(
        Stack<INamedTypeSymbol> pendingTypes,
        List<ApiType> types,
        HashSet<string> seenTypeUids,
        SymbolWalkContext context)
    {
        while (pendingTypes.TryPop(out var type))
        {
            if (!TypeForwardingHelpers.IsResolvable(type))
            {
                continue;
            }

            if (!SymbolWalkerHelpers.IsExternallyVisible(type.DeclaredAccessibility))
            {
                continue;
            }

            // C# 14 extension declarations surface as synthesised
            // grouping / marker types alongside their classic
            // [Extension] impl method on the parent container; emit
            // only the impl, drop the marker.
            if (SymbolWalkerHelpers.IsExtensionDeclaration(type))
            {
                TypeForwardingHelpers.PushNested(type, pendingTypes);
                continue;
            }

            if (TypeForwardingHelpers.IsAlreadyCollected(type, seenTypeUids))
            {
                continue;
            }

            if (TypeBuilder.TryBuild(type, context) is { } apiType)
            {
                types.Add(apiType);
                if (apiType.Uid is [_, ..])
                {
                    seenTypeUids.Add(apiType.Uid);
                }
            }

            TypeForwardingHelpers.PushNested(type, pendingTypes);
        }
    }
}
