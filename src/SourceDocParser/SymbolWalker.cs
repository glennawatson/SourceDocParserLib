// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using SourceDocParser.SourceLink;
using static Microsoft.CodeAnalysis.SymbolDisplayDelegateStyle;
using static Microsoft.CodeAnalysis.SymbolDisplayExtensionMethodStyle;
using static Microsoft.CodeAnalysis.SymbolDisplayGenericsOptions;
using static Microsoft.CodeAnalysis.SymbolDisplayGlobalNamespaceStyle;
using static Microsoft.CodeAnalysis.SymbolDisplayMemberOptions;
using static Microsoft.CodeAnalysis.SymbolDisplayMiscellaneousOptions;
using static Microsoft.CodeAnalysis.SymbolDisplayParameterOptions;
using static Microsoft.CodeAnalysis.SymbolDisplayPropertyStyle;
using static Microsoft.CodeAnalysis.SymbolDisplayTypeQualificationStyle;

namespace SourceDocParser;

/// <summary>
/// Walks an IAssemblySymbol public surface to produce an ApiCatalog.
/// Stateless apart from a per-walk DocResolver that memoizes XML doc parses.
/// Iteration uses explicit Stacks to avoid closure allocations from recursion,
/// improving performance across large assemblies.
/// </summary>
public sealed class SymbolWalker : ISymbolWalker
{
    /// <summary>
    /// Display format for full member signatures including accessibility,
    /// modifiers, parameter names, and default values.
    /// </summary>
    [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1118:Parameter should not span multiple lines", Justification = "Due to line complexity justified")]
    private static readonly SymbolDisplayFormat _signatureFormat = new(
        globalNamespaceStyle: OmittedAsContaining,
        typeQualificationStyle: NameAndContainingTypes,
        genericsOptions: IncludeTypeParameters | IncludeVariance | IncludeTypeConstraints,
        memberOptions: SymbolDisplayMemberOptions.IncludeType
                       | IncludeParameters
                       | IncludeAccessibility
                       | SymbolDisplayMemberOptions.IncludeModifiers
                       | IncludeRef
                       | IncludeExplicitInterface,
        delegateStyle: NameAndSignature,
        extensionMethodStyle: StaticMethod,
        parameterOptions: IncludeParamsRefOut
                          | SymbolDisplayParameterOptions.IncludeType
                          | IncludeName
                          | IncludeDefaultValue
                          | IncludeExtensionThis,
        propertyStyle: ShowReadWriteDescriptor,
        miscellaneousOptions: UseSpecialTypes
                              | EscapeKeywordIdentifiers
                              | IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Walks the public types of one assembly and returns the catalog for the supplied TFM.
    /// </summary>
    /// <param name="tfm">The TFM the assembly was extracted from; recorded on the catalog so downstream consumers know which compilation it came from.</param>
    /// <param name="assembly">Assembly symbol to walk.</param>
    /// <param name="compilation">Compilation that produced the assembly symbol - passed through to the DocResolver for cref resolution on inheritdoc.</param>
    /// <param name="sourceLinks">SourceLink resolver scoped to the assembly being walked. Populates <see cref="ApiMember.SourceUrl"/> and <see cref="ApiType.SourceUrl"/> when PDB + SourceLink data is available; otherwise contributes nothing and the URLs stay null.</param>
    /// <returns>The generated API catalog.</returns>
    public ApiCatalog Walk(string tfm, IAssemblySymbol assembly, Compilation compilation, ISourceLinkResolver sourceLinks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tfm);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(sourceLinks);
        return WalkCore(tfm, assembly, compilation, sourceLinks);
    }

    /// <summary>
    /// Static implementation of <see cref="Walk"/>. Kept separate so the
    /// instance method is a one-liner and the body remains pure / shareable
    /// across walker instances without per-instance state.
    /// </summary>
    /// <param name="tfm">TFM the assembly was extracted from.</param>
    /// <param name="assembly">Assembly symbol to walk.</param>
    /// <param name="compilation">Compilation that produced <paramref name="assembly"/>.</param>
    /// <param name="sourceLinks">Resolver scoped to <paramref name="assembly"/>.</param>
    /// <returns>The generated API catalog.</returns>
    private static ApiCatalog WalkCore(string tfm, IAssemblySymbol assembly, Compilation compilation, ISourceLinkResolver sourceLinks)
    {
        var context = new SymbolWalkContext(
            AssemblyName: assembly.Name,
            Tfm: tfm,
            Docs: new(compilation),
            TypeRefs: new(),
            SourceLinks: sourceLinks,
            NamespaceDisplayNames: new(SymbolEqualityComparer.Default),
            AppliesTo: [tfm]);

        // Total count isn't known upfront - typical assemblies land in
        // the low hundreds of public types — so let the list grow
        // dynamically rather than picking an arbitrary capacity.
        List<ApiType> types = [];
        var pendingNamespaces = new Stack<INamespaceSymbol>();
        var pendingTypes = new Stack<INamedTypeSymbol>();

        pendingNamespaces.Push(assembly.GlobalNamespace);

        while (pendingNamespaces.Count > 0)
        {
            var ns = pendingNamespaces.Pop();

            foreach (var nested in ns.GetNamespaceMembers())
            {
                pendingNamespaces.Push(nested);
            }

            foreach (var type in ns.GetTypeMembers())
            {
                pendingTypes.Push(type);
            }

            while (pendingTypes.Count > 0)
            {
                var type = pendingTypes.Pop();
                if (!SymbolWalkerHelpers.IsExternallyVisible(type.DeclaredAccessibility))
                {
                    continue;
                }

                if (TryBuildType(type, context) is { } apiType)
                {
                    types.Add(apiType);
                }

                foreach (var nestedType in type.GetTypeMembers())
                {
                    pendingTypes.Push(nestedType);
                }
            }
        }

        types.Sort(static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return new(tfm, types);
    }

    /// <summary>
    /// Returns the cached display string for <paramref name="ns"/>, lazily
    /// populating <see cref="SymbolWalkContext.NamespaceDisplayNames"/> on
    /// the first encounter. Without the cache every type re-formats its
    /// containing namespace via <c>ToDisplayString</c>, which is one of
    /// the heaviest per-type allocations in the walk.
    /// </summary>
    /// <param name="context">Per-walk context owning the namespace cache.</param>
    /// <param name="ns">Namespace symbol to format.</param>
    /// <returns>The cached display string, or empty for the global namespace.</returns>
    private static string GetNamespaceDisplayName(SymbolWalkContext context, INamespaceSymbol? ns)
    {
        if (ns is not { IsGlobalNamespace: false })
        {
            return string.Empty;
        }

        if (context.NamespaceDisplayNames.TryGetValue(ns, out var cached))
        {
            return cached;
        }

        var formatted = ns.ToDisplayString();
        context.NamespaceDisplayNames[ns] = formatted;
        return formatted;
    }

    /// <summary>
    /// Builds an ApiType for one Roslyn INamedTypeSymbol. Returns null for types
    /// that cannot be classified (error symbols, modules, etc.).
    /// </summary>
    /// <param name="type">Source type symbol.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The generated API type, or null if it could not be built.</returns>
    private static ApiType? TryBuildType(INamedTypeSymbol type, SymbolWalkContext context)
    {
        if (SymbolWalkerHelpers.ClassifyType(type) is not { } kind)
        {
            return null;
        }

        var uid = type.GetDocumentationCommentId() ?? string.Empty;
        var ns = GetNamespaceDisplayName(context, type.ContainingNamespace);
        var fullName = ns.Length == 0 ? type.Name : $"{ns}.{type.Name}";

        return new(
            Name: type.Name,
            FullName: fullName,
            Uid: uid,
            Namespace: ns,
            Kind: kind,
            Arity: type.Arity,
            IsStatic: type.IsStatic,
            IsSealed: type.IsSealed,
            IsAbstract: type.IsAbstract,
            IsReadOnly: type.IsReadOnly,
            IsByRefLike: type.IsRefLikeType,
            AssemblyName: context.AssemblyName,
            Documentation: context.Docs.Resolve(type),
            BaseType: SymbolWalkerHelpers.BuildBaseTypeReference(type, context.TypeRefs),
            Interfaces: SymbolWalkerHelpers.BuildInterfaceReferences(type, context.TypeRefs),
            UnionCases: SymbolWalkerHelpers.BuildUnionCases(type),
            Members: BuildMembers(type, type.Name, uid, context),
            SourceUrl: context.SourceLinks.Resolve(type),
            AppliesTo: context.AppliesTo);
    }

    /// <summary>
    /// Walks the immediate members of a type and returns the documented ones.
    /// Skips compiler-generated and non-public members.
    /// </summary>
    /// <param name="type">Containing type.</param>
    /// <param name="containingTypeName">Display name of the containing type.</param>
    /// <param name="containingTypeUid">Roslyn UID of the containing type.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The list of documented members.</returns>
    private static List<ApiMember> BuildMembers(
        INamedTypeSymbol type,
        string containingTypeName,
        string containingTypeUid,
        SymbolWalkContext context)
    {
        // Pre-size to the raw member count from Roslyn - we'll filter
        // some out (non-public, implicitly declared, unsupported kinds)
        // but it's a tight upper bound that avoids List growth on the
        // chunkier types.
        var rawMembers = type.GetMembers();
        var members = new List<ApiMember>(rawMembers.Length);

        for (var i = 0; i < rawMembers.Length; i++)
        {
            var member = rawMembers[i];
            if (member.IsImplicitlyDeclared || !SymbolWalkerHelpers.IsExternallyVisible(member.DeclaredAccessibility))
            {
                continue;
            }

            if (SymbolWalkerHelpers.TryClassifyMember(member) is not { } kind)
            {
                continue;
            }

            var uid = member.GetDocumentationCommentId() ?? string.Empty;
            members.Add(new(
                Name: member.Name,
                Uid: uid,
                Kind: kind,
                IsStatic: member.IsStatic,
                IsExtension: member is IMethodSymbol { IsExtensionMethod: true },
                IsRequired: SymbolWalkerHelpers.IsRequiredMember(member),
                IsVirtual: member.IsVirtual,
                IsOverride: member.IsOverride,
                IsAbstract: member.IsAbstract,
                IsSealed: member.IsSealed,
                Signature: member.ToDisplayString(_signatureFormat),
                Parameters: SymbolWalkerHelpers.BuildParameters(member, context.TypeRefs),
                TypeParameters: SymbolWalkerHelpers.BuildTypeParameters(member),
                ReturnType: SymbolWalkerHelpers.BuildReturnTypeReference(member, context.TypeRefs),
                ContainingTypeUid: containingTypeUid,
                ContainingTypeName: containingTypeName,
                SourceUrl: context.SourceLinks.Resolve(member),
                Documentation: context.Docs.Resolve(member)));
        }

        return members;
    }
}
