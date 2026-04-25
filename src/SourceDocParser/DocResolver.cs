// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Xml;
using Microsoft.CodeAnalysis;

namespace SourceDocParser;

/// <summary>
/// Resolves merged <see cref="ApiDocumentation"/> for symbols by combining
/// XML parsing, <c>inheritdoc</c> walking, and auto-inheritance.
/// Results are memoized in a per-resolver cache to ensure efficient
/// resolution across complex inheritance chains. Created per assembly.
/// </summary>
/// <param name="compilation">Compilation used for cref resolution.</param>
internal sealed class DocResolver(Compilation compilation)
{
    /// <summary>
    /// Symbol -> resolved documentation. Concurrent so the parallel
    /// per-DLL walker doesn't need an external lock.
    /// </summary>
    private static readonly ConcurrentDictionary<CrefResolutionContext, ApiDocumentation> _cache = new();

    /// <summary>
    /// Cached XmlReaderSettings used for every per-symbol parse.
    /// DTD processing is disabled (XML doc files don't use DTDs and
    /// processing them is a known XXE vector); comments and PIs
    /// are skipped because doc files don't carry meaningful ones.
    /// </summary>
    private static readonly XmlReaderSettings _readerSettings = new()
    {
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Ignore,
    };

    /// <summary>
    /// Returns the resolved documentation for a symbol, parsing and
    /// inheriting on first request and reusing on every subsequent
    /// call. Returns <see cref="ApiDocumentation.Empty"/> when the
    /// symbol has no doc text of its own and nothing inheritable.
    /// </summary>
    /// <param name="symbol">Symbol whose documentation to resolve.</param>
    /// <returns>The resolved documentation.</returns>
    public ApiDocumentation Resolve(ISymbol symbol) =>
        _cache.GetOrAdd(new(symbol, compilation), static x => ResolveCore(x.Symbol, x.Compilation));

    /// <summary>
    /// Returns the resolved documentation for a symbol, parsing and
    /// inheriting on first request and reusing on every subsequent
    /// call. Returns <see cref="ApiDocumentation.Empty"/> when the
    /// symbol has no doc text of its own and nothing inheritable.
    /// </summary>
    /// <param name="symbol">Symbol whose documentation to resolve.</param>
    /// <param name="compilation">Compilation used for cref resolution.</param>
    /// <returns>The resolved documentation.</returns>
    private static ApiDocumentation Resolve(ISymbol symbol, Compilation compilation) =>
        _cache.GetOrAdd(new(symbol, compilation), static x => ResolveCore(x.Symbol, x.Compilation));

    /// <summary>
    /// Resolution body: parse the symbol's own XML, decide whether
    /// to inherit (explicit inheritdoc tag, or auto-inherit on a
    /// fully-empty override / interface impl), walk the chain with
    /// cycle protection, and merge child-wins-per-field.
    /// </summary>
    /// <param name="symbol">Symbol whose XML doc to resolve.</param>
    /// <param name="compilation">Compilation used for cref resolution.</param>
    /// <returns>The resolved documentation.</returns>
    private static ApiDocumentation ResolveCore(ISymbol symbol, Compilation compilation)
    {
        var raw = ParseRaw(symbol);

        if (raw.HasInheritDoc)
        {
            // Explicit inheritdoc. Walk the chain with cycle protection;
            // child fields beat parent fields.
            var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { symbol };
            return ResolveExplicitInherit(symbol, raw, visited, compilation);
        }

        if (!raw.IsCompletelyEmpty || FindNaturalInheritedDocSource(symbol) is not { } autoSource)
        {
            return ToApiDocumentation(raw, inheritedFrom: null);
        }

        // Auto-inherit: the symbol has no docs but does override
        // or implement something. Surface the parent's docs and
        // tell the emitter where they came from.
        var parentDoc = Resolve(autoSource, compilation);
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
    /// <param name="compilation">Compilation used for cref resolution.</param>
    /// <returns>The resolved documentation.</returns>
    private static ApiDocumentation ResolveExplicitInherit(ISymbol symbol, RawDocumentation raw, HashSet<ISymbol> visited, Compilation compilation)
    {
        var inheritFrom = raw.InheritDocCref is { Length: > 0 } cref
            ? ResolveCref(cref, compilation)
            : FindNaturalInheritedDocSource(symbol);

        if (inheritFrom is null || !visited.Add(inheritFrom))
        {
            // No source or we've already visited it (cycle). Just
            // emit what we have without an inheritedFrom marker;
            // a cycle isn't user-actionable noise.
            return ToApiDocumentation(raw, inheritedFrom: null);
        }

        var parentDoc = Resolve(inheritFrom, compilation);
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
    private static ISymbol? FindNaturalInheritedDocSource(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { OverriddenMethod: { } overridden } => overridden,
        IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 } m => m.ExplicitInterfaceImplementations[0],
        IMethodSymbol m => FindImplicitInterfaceImpl(m),

        IPropertySymbol { OverriddenProperty: { } overridden } => overridden,
        IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 } p => p.ExplicitInterfaceImplementations[0],
        IPropertySymbol p => FindImplicitInterfaceImpl(p),

        IEventSymbol { OverriddenEvent: { } overridden } => overridden,
        IEventSymbol { ExplicitInterfaceImplementations.Length: > 0 } e => e.ExplicitInterfaceImplementations[0],
        IEventSymbol e => FindImplicitInterfaceImpl(e),

        INamedTypeSymbol { BaseType: { } baseType } => baseType,
        _ => null,
    };

    /// <summary>
    /// Walks the containing type's interfaces looking for a member
    /// whose implementation is the supplied symbol. Iterates the
    /// underlying ImmutableArrays with for-loops to avoid the
    /// foreach struct-enumerator overhead that adds up across
    /// thousands of resolutions.
    /// </summary>
    /// <param name="symbol">Symbol to identify as an interface impl.</param>
    /// <returns>The interface member being implemented, or null if none found.</returns>
    private static ISymbol? FindImplicitInterfaceImpl(ISymbol symbol)
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
    /// type, used by the emitter's "Inherited from …" indicator.
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
            Summary: child.Summary.Length > 0 ? child.Summary : parent.Summary,
            Remarks: child.Remarks.Length > 0 ? child.Remarks : parent.Remarks,
            Returns: child.Returns.Length > 0 ? child.Returns : parent.Returns,
            Value: child.Value.Length > 0 ? child.Value : parent.Value,
            Examples: child.Examples.Count > 0 ? child.Examples : parent.Examples,
            Parameters: MergeKeyed(child.Parameters, parent.Parameters),
            TypeParameters: MergeKeyed(child.TypeParameters, parent.TypeParameters),
            Exceptions: MergeKeyed(child.Exceptions, parent.Exceptions),
            SeeAlso: child.SeeAlso.Count > 0 ? child.SeeAlso : parent.SeeAlso,
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
    private static List<DocEntry> MergeKeyed(
        List<DocEntry> child,
        List<DocEntry> parent)
    {
        if (child.Count == 0)
        {
            return parent;
        }

        if (parent.Count == 0)
        {
            return child;
        }

        var childKeys = new HashSet<string>(child.Count, StringComparer.Ordinal);
        for (var i = 0; i < child.Count; i++)
        {
            childKeys.Add(child[i].Name);
        }

        var merged = new List<DocEntry>(child.Count + parent.Count);
        for (var i = 0; i < child.Count; i++)
        {
            merged.Add(child[i]);
        }

        for (var i = 0; i < parent.Count; i++)
        {
            if (!childKeys.Contains(parent[i].Name))
            {
                merged.Add(parent[i]);
            }
        }

        return merged;
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
    /// <returns>The parsed raw documentation.</returns>
    private static RawDocumentation ParseRaw(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        return string.IsNullOrEmpty(xml) ? RawDocumentation.Empty : Parse(xml);
    }

    /// <summary>
    /// Parses one <c>member</c> XML fragment into a
    /// RawDocumentation. Streams via XmlReader rather than building
    /// an XDocument so we don't materialise a full DOM per symbol -
    /// the doc fragment is small but with thousands of symbols the
    /// allocation savings add up. The single pass also captures
    /// inheritdoc presence and its optional cref attribute.
    /// </summary>
    /// <param name="memberXml">Raw <c>member</c> XML.</param>
    /// <returns>The parsed raw documentation.</returns>
    private static RawDocumentation Parse(string memberXml)
    {
        var summary = string.Empty;
        var remarks = string.Empty;
        var returns = string.Empty;
        var value = string.Empty;
        List<string> examples = [];
        List<DocEntry> parameters = [];
        List<DocEntry> typeParameters = [];
        List<DocEntry> exceptions = [];
        List<string> seeAlso = [];
        var hasInheritDoc = false;
        string? inheritDocCref = null;

        using var stringReader = new StringReader(memberXml);
        using var reader = XmlReader.Create(stringReader, _readerSettings);

        while (reader.Read())
        {
            if (reader.NodeType is not XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.Name)
            {
                case "summary":
                    {
                        summary = ReadInner(reader);
                        break;
                    }

                case "remarks":
                    {
                        remarks = ReadInner(reader);
                        break;
                    }

                case "returns":
                    {
                        returns = ReadInner(reader);
                        break;
                    }

                case "value":
                    {
                        value = ReadInner(reader);
                        break;
                    }

                case "example":
                    {
                        examples.Add(ReadInner(reader));
                        break;
                    }

                case "param" when reader.GetAttribute("name") is { Length: > 0 } paramName:
                    {
                        parameters.Add(new(paramName, ReadInner(reader)));
                        break;
                    }

                case "typeparam" when reader.GetAttribute("name") is { Length: > 0 } typeParamName:
                    {
                        typeParameters.Add(new(typeParamName, ReadInner(reader)));
                        break;
                    }

                case "exception" when reader.GetAttribute("cref") is { Length: > 0 } exceptionCref:
                    {
                        exceptions.Add(new(exceptionCref, ReadInner(reader)));
                        break;
                    }

                case "seealso" when reader.GetAttribute("cref") is { Length: > 0 } seeAlsoCref:
                    {
                        seeAlso.Add(seeAlsoCref);
                        break;
                    }

                case "inheritdoc":
                    {
                        hasInheritDoc = true;
                        if (reader.GetAttribute("cref") is { Length: > 0 } inheritCref)
                        {
                            inheritDocCref = inheritCref;
                        }

                        if (!reader.IsEmptyElement)
                        {
                            // <inheritdoc>…</inheritdoc> with content: skip
                            // to the matching end tag rather than processing
                            // the body, which is rarely used and not part
                            // of any standard.
                            reader.Skip();
                        }

                        break;
                    }
            }
        }

        return new(
            Summary: summary,
            Remarks: remarks,
            Returns: returns,
            Value: value,
            Examples: examples,
            Parameters: parameters,
            TypeParameters: typeParameters,
            Exceptions: exceptions,
            SeeAlso: seeAlso,
            HasInheritDoc: hasInheritDoc,
            InheritDocCref: inheritDocCref);
    }

    /// <summary>
    /// Reads the inner XML of the current element and converts it
    /// to Markdown via XmlDocToMarkdown. Embedded inline tags like
    /// <c>see cref="..."/</c> become Zensical autorefs links
    /// at this point so the emitter can paste the result straight
    /// into a page without further XML processing.
    /// </summary>
    /// <param name="reader">Reader positioned on the element's start tag.</param>
    /// <returns>The inner content as a string.</returns>
    private static string ReadInner(XmlReader reader) => reader.IsEmptyElement ? string.Empty : XmlDocToMarkdown.Convert(reader.ReadInnerXml());

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

    /// <summary>
    /// Cache key for <see cref="ResolveCref"/>.
    /// </summary>
    /// <param name="Symbol">The symbol to compare.</param>
    /// <param name="Compilation">The compilation unit.</param>
    private sealed record CrefResolutionContext(ISymbol Symbol, Compilation Compilation)
    {
        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(SymbolEqualityComparer.Default.GetHashCode(Symbol), Compilation);

        /// <summary>
        /// Checks to make sure that the two references are equal.
        /// </summary>
        /// <param name="other">The other element.</param>
        /// <returns>True if the two values are equal.</returns>
        public bool Equals(CrefResolutionContext? other) => other is not null &&
            SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol) &&
            Compilation == other.Compilation;
    }
}
