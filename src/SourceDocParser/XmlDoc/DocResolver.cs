// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using SourceDocParser.Model;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Resolves merged <see cref="ApiDocumentation"/> for symbols by combining
/// XML parsing, <c>inheritdoc</c> walking, and auto-inheritance.
/// Results are memoized in a per-resolver cache to ensure efficient
/// resolution across complex inheritance chains. Created per assembly.
/// </summary>
public sealed class DocResolver : IDocResolver
{
    /// <summary>Per-resolver state bundle threaded through every static helper.</summary>
    private readonly DocResolveContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocResolver"/> class.
    /// </summary>
    /// <param name="compilation">Compilation used for cref resolution.</param>
    /// <remarks>
    /// The resolver returns <see cref="ApiDocumentation"/> with raw
    /// XML doc fragments; emitters convert them via
    /// <see cref="XmlDocToMarkdown"/> at render time.
    /// </remarks>
    public DocResolver(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        _context = new(compilation, new());
    }

    /// <inheritdoc />
    public ApiDocumentation Resolve(ISymbol symbol) => ResolveCached(symbol, _context);

    /// <summary>
    /// Cache-aware entry point used both by the public seam and by the
    /// recursive inheritdoc walker so the chain hits the cache.
    /// </summary>
    /// <param name="symbol">Symbol whose documentation to resolve.</param>
    /// <param name="context">Per-resolver state bundle.</param>
    /// <returns>The resolved documentation.</returns>
    internal static ApiDocumentation ResolveCached(ISymbol symbol, DocResolveContext context) =>
        context.Cache.GetOrAdd(symbol, context, static (candidate, state) => ResolveCore(candidate, state));

    /// <summary>
    /// Resolution body: parse the symbol's own XML, decide whether
    /// to inherit (explicit inheritdoc tag, or auto-inherit on a
    /// fully-empty override / interface impl), walk the chain with
    /// cycle protection, and merge child-wins-per-field.
    /// </summary>
    /// <param name="symbol">Symbol whose XML doc to resolve.</param>
    /// <param name="context">Per-resolver state bundle.</param>
    /// <returns>The resolved documentation.</returns>
    internal static ApiDocumentation ResolveCore(ISymbol symbol, DocResolveContext context)
    {
        var raw = ParseRaw(symbol, context);

        if (raw.HasInheritDoc)
        {
            // Explicit inheritdoc. Walk the chain with cycle protection;
            // child fields beat parent fields.
            var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { symbol };
            return ResolveExplicitInherit(symbol, raw, visited, context);
        }

        if (!raw.IsCompletelyEmpty || FindNaturalInheritedDocSource(symbol) is not { } autoSource)
        {
            return ToApiDocumentation(raw, inheritedFrom: null);
        }

        // Auto-inherit: the symbol has no docs but does override
        // or implement something. Surface the parent's docs and
        // tell the emitter where they came from.
        var parentDoc = ResolveCached(autoSource, context);
        return parentDoc.IsEmpty
            ? ApiDocumentation.Empty
            : parentDoc with { InheritedFrom = ContainingTypeDisplayName(autoSource) };
    }

    /// <summary>
    /// Resolves an <c>inheritdoc/</c>-bearing doc by walking
    /// to the source (cref-specified or natural chain), recursively
    /// resolving the parent (cache hit when already done), and merging
    /// per field with child fields winning.
    /// </summary>
    /// <param name="symbol">Symbol being resolved.</param>
    /// <param name="raw">Symbol's parsed own XML.</param>
    /// <param name="visited">Cycle-protection set; the caller seeded it with the original symbol.</param>
    /// <param name="context">Per-resolver state bundle.</param>
    /// <returns>The resolved documentation.</returns>
    internal static ApiDocumentation ResolveExplicitInherit(ISymbol symbol, RawDocumentation raw, HashSet<ISymbol> visited, DocResolveContext context)
    {
        var inheritFrom = raw.InheritDocCref is { Length: > 0 } cref
            ? ResolveCref(cref, context.Compilation)
            : FindNaturalInheritedDocSource(symbol);

        if (inheritFrom is null || !visited.Add(inheritFrom))
        {
            // No source or we've already visited it (cycle). Just
            // emit what we have without an inheritedFrom marker;
            // a cycle isn't user-actionable noise.
            return ToApiDocumentation(raw, inheritedFrom: null);
        }

        var parentDoc = ResolveCached(inheritFrom, context);
        return MergeWithParent(raw, parentDoc, ContainingTypeDisplayName(inheritFrom));
    }

    /// <summary>
    /// Returns the symbol whose doc should be inherited under the
    /// natural-chain rules: an override goes to the overridden
    /// member, an explicit interface impl to the named interface
    /// member, an implicit interface impl is found by asking the
    /// containing type which interface members it satisfies, and a
    /// type goes to its base type.
    /// </summary>
    /// <param name="symbol">Symbol to find an inheritance source for.</param>
    /// <returns>The symbol to inherit from, or null if no natural source exists.</returns>
    internal static ISymbol? FindNaturalInheritedDocSource(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method)
        {
            return FindNaturalInheritedMethodSource(method);
        }

        if (symbol is IPropertySymbol property)
        {
            return FindNaturalInheritedPropertySource(property);
        }

        if (symbol is IEventSymbol eventSymbol)
        {
            return FindNaturalInheritedEventSource(eventSymbol);
        }

        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        return FindNaturalInheritedTypeSource(typeSymbol);
    }

    /// <summary>
    /// Finds the natural inheritdoc source for a method symbol.
    /// </summary>
    /// <param name="symbol">Method symbol to inspect.</param>
    /// <returns>The symbol to inherit from, or null if none exists.</returns>
    internal static ISymbol? FindNaturalInheritedMethodSource(IMethodSymbol symbol)
    {
        if (symbol.OverriddenMethod is { } overridden)
        {
            return overridden;
        }

        if (symbol.ExplicitInterfaceImplementations is [var implementation, ..])
        {
            return implementation;
        }

        return FindImplicitInterfaceImpl(symbol);
    }

    /// <summary>
    /// Finds the natural inheritdoc source for a property symbol.
    /// </summary>
    /// <param name="symbol">Property symbol to inspect.</param>
    /// <returns>The symbol to inherit from, or null if none exists.</returns>
    internal static ISymbol? FindNaturalInheritedPropertySource(IPropertySymbol symbol)
    {
        if (symbol.OverriddenProperty is { } overridden)
        {
            return overridden;
        }

        if (symbol.ExplicitInterfaceImplementations is [var implementation, ..])
        {
            return implementation;
        }

        return FindImplicitInterfaceImpl(symbol);
    }

    /// <summary>
    /// Finds the natural inheritdoc source for an event symbol.
    /// </summary>
    /// <param name="symbol">Event symbol to inspect.</param>
    /// <returns>The symbol to inherit from, or null if none exists.</returns>
    internal static ISymbol? FindNaturalInheritedEventSource(IEventSymbol symbol)
    {
        if (symbol.OverriddenEvent is { } overridden)
        {
            return overridden;
        }

        if (symbol.ExplicitInterfaceImplementations is [var implementation, ..])
        {
            return implementation;
        }

        return FindImplicitInterfaceImpl(symbol);
    }

    /// <summary>
    /// Finds the natural inheritdoc source for a type symbol.
    /// </summary>
    /// <param name="symbol">Type symbol to inspect.</param>
    /// <returns>The base type, or null if none exists.</returns>
    internal static ISymbol? FindNaturalInheritedTypeSource(INamedTypeSymbol symbol) => symbol.BaseType;

    /// <summary>
    /// Walks the containing type's interfaces looking for a member
    /// whose implementation is the supplied symbol. Iterates the
    /// underlying ImmutableArrays with for-loops to avoid the
    /// foreach struct-enumerator overhead that adds up across
    /// thousands of resolutions.
    /// </summary>
    /// <param name="symbol">Symbol to identify as an interface impl.</param>
    /// <returns>The interface member being implemented, or null if none found.</returns>
    internal static ISymbol? FindImplicitInterfaceImpl(ISymbol symbol)
    {
        var containingType = symbol.ContainingType;
        if (containingType is null)
        {
            return null;
        }

        var interfaces = containingType.AllInterfaces;
        for (var i = 0; i < interfaces.Length; i++)
        {
            var members = interfaces[i].GetMembers();
            for (var j = 0; j < members.Length; j++)
            {
                var ifaceMember = members[j];
                var impl = containingType.FindImplementationForInterfaceMember(ifaceMember);
                if (SymbolEqualityComparer.Default.Equals(impl, symbol))
                {
                    return ifaceMember;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a friendly display name for a symbol's containing
    /// type, used by the emitter's "Inherited from ..." indicator.
    /// Falls back to the symbol's own name for type symbols (where
    /// "containing type" is null).
    /// </summary>
    /// <param name="symbol">Symbol to display.</param>
    /// <returns>The display name of the containing type.</returns>
    private static string ContainingTypeDisplayName(ISymbol symbol) => symbol.ContainingType is { } t
        ? t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        : symbol.Name;

    /// <summary>
    /// Merges a child's parsed doc with its resolved parent: per-field
    /// the child wins where it has content, otherwise the parent's
    /// value passes through. Keyed lists (parameters, type parameters,
    /// exceptions) merge per-key with child entries shadowing parent
    /// entries that share the same key.
    /// </summary>
    /// <param name="child">Child symbol's parsed doc.</param>
    /// <param name="parent">Parent symbol's already-resolved doc.</param>
    /// <param name="inheritedFromName">Display name to surface on the merged doc.</param>
    /// <returns>The merged documentation.</returns>
    private static ApiDocumentation MergeWithParent(RawDocumentation child, ApiDocumentation parent, string inheritedFromName) =>
        new(
            Summary: child.Summary is [_, ..] ? child.Summary : parent.Summary,
            Remarks: child.Remarks is [_, ..] ? child.Remarks : parent.Remarks,
            Returns: child.Returns is [_, ..] ? child.Returns : parent.Returns,
            Value: child.Value is [_, ..] ? child.Value : parent.Value,
            Examples: child.Examples is [_, ..] ? child.Examples : parent.Examples,
            Parameters: MergeKeyed(child.Parameters, parent.Parameters),
            TypeParameters: MergeKeyed(child.TypeParameters, parent.TypeParameters),
            Exceptions: MergeKeyed(child.Exceptions, parent.Exceptions),
            SeeAlso: child.SeeAlso is [_, ..] ? child.SeeAlso : parent.SeeAlso,
            InheritedFrom: inheritedFromName);

    /// <summary>
    /// Merges two ordered key/value lists with child entries winning
    /// over parent entries on key collision. Pre-builds an ordinal
    /// HashSet of child keys so the parent scan is O(1) per entry.
    /// Allocates only the result list and the small key set; both
    /// are sized to the known counts.
    /// </summary>
    /// <param name="child">Child entries (preserved in order, all included).</param>
    /// <param name="parent">Parent entries (only those with keys not in child are appended).</param>
    /// <returns>The merged list of entries.</returns>
    private static DocEntry[] MergeKeyed(
        DocEntry[] child,
        DocEntry[] parent)
    {
        if (child is [])
        {
            return parent;
        }

        if (parent is [])
        {
            return child;
        }

        var childKeys = new HashSet<string>(child.Length, StringComparer.Ordinal);
        for (var i = 0; i < child.Length; i++)
        {
            childKeys.Add(child[i].Name);
        }

        var merged = new List<DocEntry>(child.Length + parent.Length);
        for (var i = 0; i < child.Length; i++)
        {
            merged.Add(child[i]);
        }

        for (var i = 0; i < parent.Length; i++)
        {
            if (!childKeys.Contains(parent[i].Name))
            {
                merged.Add(parent[i]);
            }
        }

        return [.. merged];
    }

    /// <summary>
    /// Converts a RawDocumentation to the public ApiDocumentation
    /// shape, attaching the supplied inheritance marker. Direct
    /// projection - no work beyond record construction.
    /// </summary>
    /// <param name="raw">Parsed raw doc.</param>
    /// <param name="inheritedFrom">Marker to surface, or null.</param>
    /// <returns>The API documentation record.</returns>
    private static ApiDocumentation ToApiDocumentation(RawDocumentation raw, string? inheritedFrom) =>
        new(
            Summary: raw.Summary,
            Remarks: raw.Remarks,
            Returns: raw.Returns,
            Value: raw.Value,
            Examples: raw.Examples,
            Parameters: raw.Parameters,
            TypeParameters: raw.TypeParameters,
            Exceptions: raw.Exceptions,
            SeeAlso: raw.SeeAlso,
            InheritedFrom: inheritedFrom);

    /// <summary>
    /// Pulls the symbol's raw XML doc text and parses it. Returns
    /// <see cref="RawDocumentation.Empty"/> for symbols with no
    /// shipped XML (the very common case for internal types whose
    /// docs were never written).
    /// </summary>
    /// <param name="symbol">Symbol whose XML doc to read.</param>
    /// <param name="context">Per-resolver state bundle.</param>
    /// <returns>The parsed raw documentation.</returns>
    private static RawDocumentation ParseRaw(ISymbol symbol, DocResolveContext context)
    {
        var xml = symbol.GetDocumentationCommentXml();
        return xml is [_, ..] ? DocXmlParser.Parse(xml, context) : RawDocumentation.Empty;
    }

    /// <summary>
    /// Resolves an <c>inheritdoc cref="..."/</c> target via
    /// Roslyn's <see cref="DocumentationCommentId.GetFirstSymbolForDeclarationId"/>.
    /// Returns null when the cref doesn't bind to anything in our
    /// compilation (e.g. it points at a private member or a type we
    /// don't reference).
    /// </summary>
    /// <param name="cref">cref string from the inheritdoc element.</param>
    /// <param name="compilation">Compilation to use for resolution.</param>
    /// <returns>The resolved symbol, or null if not found.</returns>
    private static ISymbol? ResolveCref(string cref, Compilation compilation) =>
        DocumentationCommentId.GetFirstSymbolForDeclarationId(cref, compilation);
}
