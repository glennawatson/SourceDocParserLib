// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using SourceDocParser.Model;

namespace SourceDocParser.Walk;

/// <summary>
/// Converts a Roslyn <see cref="INamedTypeSymbol"/> into the
/// appropriate <see cref="ApiType"/> derivation — union, enum,
/// delegate, or object (class / struct / interface / record /
/// record struct). Returns <see langword="null"/> when the symbol
/// can't be classified (error symbols, modules). All shared
/// extraction work — namespace resolution, base / interface refs,
/// attributes, members, extension blocks — is delegated to focused
/// helpers so each can be unit-tested independently.
/// </summary>
internal static class TypeBuilder
{
    /// <summary>
    /// Builds the <see cref="ApiType"/> for <paramref name="type"/>.
    /// </summary>
    /// <param name="type">Source type symbol.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The generated API type, or null when classification failed.</returns>
    internal static ApiType? TryBuild(INamedTypeSymbol type, SymbolWalkContext context)
    {
        var uid = type.GetDocumentationCommentId() ?? string.Empty;
        var ns = NamespaceDisplayResolver.Resolve(context, type.ContainingNamespace);
        var fullName = BuildFullName(type, ns);
        var documentation = context.Docs.Resolve(type);
        var baseTypeRef = SymbolWalkerHelpers.BuildBaseTypeReference(type, context.TypeRefs);
        var interfaces = SymbolWalkerHelpers.BuildInterfaceReferences(type, context.TypeRefs);
        var sourceUrl = context.SourceLinks.Resolve(type);
        var (attributes, isObsolete, obsoleteMessage) = AttributeExtractor.ExtractAll(type);

        return type.TypeKind switch
        {
            // Closed-hierarchy unions (C# 15+) are class-shaped and have to
            // be checked before the generic class branch — otherwise we'd
            // emit them as a plain class and lose the case list.
            TypeKind.Class when SymbolWalkerHelpers.IsUnion(type) => BuildUnion(
                type,
                uid,
                ns,
                fullName,
                documentation,
                baseTypeRef,
                interfaces,
                sourceUrl,
                attributes,
                isObsolete,
                obsoleteMessage,
                context),
            TypeKind.Enum => BuildEnum(
                type,
                uid,
                ns,
                fullName,
                documentation,
                baseTypeRef,
                interfaces,
                sourceUrl,
                attributes,
                isObsolete,
                obsoleteMessage,
                context),
            TypeKind.Delegate => BuildDelegate(
                type,
                uid,
                ns,
                fullName,
                documentation,
                baseTypeRef,
                interfaces,
                sourceUrl,
                attributes,
                isObsolete,
                obsoleteMessage,
                context),
            _ => BuildObject(
                type,
                uid,
                ns,
                fullName,
                documentation,
                baseTypeRef,
                interfaces,
                sourceUrl,
                attributes,
                isObsolete,
                obsoleteMessage,
                context),
        };
    }

    /// <summary>Constructs the <see cref="ApiUnionType"/> branch.</summary>
    /// <param name="type">Source type symbol.</param>
    /// <param name="uid">Documentation comment id.</param>
    /// <param name="ns">Containing namespace display string.</param>
    /// <param name="fullName">Namespace-qualified name.</param>
    /// <param name="documentation">Pre-resolved documentation.</param>
    /// <param name="baseTypeRef">Base type reference.</param>
    /// <param name="interfaces">Interface references.</param>
    /// <param name="sourceUrl">Source URL or null.</param>
    /// <param name="attributes">Pre-extracted attributes.</param>
    /// <param name="isObsolete">Whether <c>[Obsolete]</c> was applied.</param>
    /// <param name="obsoleteMessage">Optional <c>[Obsolete]</c> message.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The constructed union type.</returns>
    internal static ApiUnionType BuildUnion(
        INamedTypeSymbol type,
        string uid,
        string ns,
        string fullName,
        ApiDocumentation documentation,
        ApiTypeReference? baseTypeRef,
        ApiTypeReference[] interfaces,
        string? sourceUrl,
        ApiAttribute[] attributes,
        bool isObsolete,
        string? obsoleteMessage,
        SymbolWalkContext context) => new(
            Name: type.Name,
            FullName: fullName,
            Uid: uid,
            Namespace: ns,
            Arity: type.Arity,
            IsStatic: type.IsStatic,
            IsSealed: type.IsSealed,
            IsAbstract: type.IsAbstract,
            AssemblyName: context.AssemblyName,
            Documentation: documentation,
            BaseType: baseTypeRef,
            Interfaces: interfaces,
            SourceUrl: sourceUrl,
            AppliesTo: context.AppliesTo,
            IsObsolete: isObsolete,
            ObsoleteMessage: obsoleteMessage,
            Attributes: attributes,
            Members: MemberBuilder.Build(type, type.Name, uid, context),
            Cases: SymbolWalkerHelpers.BuildUnionCases(type, context.TypeRefs));

    /// <summary>Constructs the <see cref="ApiEnumType"/> branch.</summary>
    /// <param name="type">Source type symbol.</param>
    /// <param name="uid">Documentation comment id.</param>
    /// <param name="ns">Containing namespace display string.</param>
    /// <param name="fullName">Namespace-qualified name.</param>
    /// <param name="documentation">Pre-resolved documentation.</param>
    /// <param name="baseTypeRef">Base type reference.</param>
    /// <param name="interfaces">Interface references.</param>
    /// <param name="sourceUrl">Source URL or null.</param>
    /// <param name="attributes">Pre-extracted attributes.</param>
    /// <param name="isObsolete">Whether <c>[Obsolete]</c> was applied.</param>
    /// <param name="obsoleteMessage">Optional <c>[Obsolete]</c> message.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The constructed enum type.</returns>
    internal static ApiEnumType BuildEnum(
        INamedTypeSymbol type,
        string uid,
        string ns,
        string fullName,
        ApiDocumentation documentation,
        ApiTypeReference? baseTypeRef,
        ApiTypeReference[] interfaces,
        string? sourceUrl,
        ApiAttribute[] attributes,
        bool isObsolete,
        string? obsoleteMessage,
        SymbolWalkContext context) => new(
            Name: type.Name,
            FullName: fullName,
            Uid: uid,
            Namespace: ns,
            Arity: type.Arity,
            IsStatic: type.IsStatic,
            IsSealed: type.IsSealed,
            IsAbstract: type.IsAbstract,
            AssemblyName: context.AssemblyName,
            Documentation: documentation,
            BaseType: baseTypeRef,
            Interfaces: interfaces,
            SourceUrl: sourceUrl,
            AppliesTo: context.AppliesTo,
            IsObsolete: isObsolete,
            ObsoleteMessage: obsoleteMessage,
            Attributes: attributes,
            UnderlyingType: context.TypeRefs.GetOrAdd(
                type.EnumUnderlyingType ?? type,
                SymbolWalkerHelpers.BuildReference),
            Values: SymbolWalkerHelpers.BuildEnumValues(type, context));

    /// <summary>Constructs the <see cref="ApiDelegateType"/> branch.</summary>
    /// <param name="type">Source type symbol.</param>
    /// <param name="uid">Documentation comment id.</param>
    /// <param name="ns">Containing namespace display string.</param>
    /// <param name="fullName">Namespace-qualified name.</param>
    /// <param name="documentation">Pre-resolved documentation.</param>
    /// <param name="baseTypeRef">Base type reference.</param>
    /// <param name="interfaces">Interface references.</param>
    /// <param name="sourceUrl">Source URL or null.</param>
    /// <param name="attributes">Pre-extracted attributes.</param>
    /// <param name="isObsolete">Whether <c>[Obsolete]</c> was applied.</param>
    /// <param name="obsoleteMessage">Optional <c>[Obsolete]</c> message.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The constructed delegate type.</returns>
    internal static ApiDelegateType BuildDelegate(
        INamedTypeSymbol type,
        string uid,
        string ns,
        string fullName,
        ApiDocumentation documentation,
        ApiTypeReference? baseTypeRef,
        ApiTypeReference[] interfaces,
        string? sourceUrl,
        ApiAttribute[] attributes,
        bool isObsolete,
        string? obsoleteMessage,
        SymbolWalkContext context) => new(
            Name: type.Name,
            FullName: fullName,
            Uid: uid,
            Namespace: ns,
            Arity: type.Arity,
            IsStatic: type.IsStatic,
            IsSealed: type.IsSealed,
            IsAbstract: type.IsAbstract,
            AssemblyName: context.AssemblyName,
            Documentation: documentation,
            BaseType: baseTypeRef,
            Interfaces: interfaces,
            SourceUrl: sourceUrl,
            AppliesTo: context.AppliesTo,
            IsObsolete: isObsolete,
            ObsoleteMessage: obsoleteMessage,
            Attributes: attributes,
            Invoke: SymbolWalkerHelpers.BuildDelegateInvoke(type, context));

    /// <summary>Constructs the <see cref="ApiObjectType"/> branch — class / struct / interface / record / record struct.</summary>
    /// <param name="type">Source type symbol.</param>
    /// <param name="uid">Documentation comment id.</param>
    /// <param name="ns">Containing namespace display string.</param>
    /// <param name="fullName">Namespace-qualified name.</param>
    /// <param name="documentation">Pre-resolved documentation.</param>
    /// <param name="baseTypeRef">Base type reference.</param>
    /// <param name="interfaces">Interface references.</param>
    /// <param name="sourceUrl">Source URL or null.</param>
    /// <param name="attributes">Pre-extracted attributes.</param>
    /// <param name="isObsolete">Whether <c>[Obsolete]</c> was applied.</param>
    /// <param name="obsoleteMessage">Optional <c>[Obsolete]</c> message.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The constructed object type, or null when no <see cref="ApiObjectKind"/> matches (e.g. error symbols).</returns>
    internal static ApiObjectType? BuildObject(
        INamedTypeSymbol type,
        string uid,
        string ns,
        string fullName,
        ApiDocumentation documentation,
        ApiTypeReference? baseTypeRef,
        ApiTypeReference[] interfaces,
        string? sourceUrl,
        ApiAttribute[] attributes,
        bool isObsolete,
        string? obsoleteMessage,
        SymbolWalkContext context) =>
        SymbolWalkerHelpers.ClassifyObjectKind(type) is not { } kind
            ? null
            : new ApiObjectType(
                Name: type.Name,
                FullName: fullName,
                Uid: uid,
                Namespace: ns,
                Arity: type.Arity,
                IsStatic: type.IsStatic,
                IsSealed: type.IsSealed,
                IsAbstract: type.IsAbstract,
                AssemblyName: context.AssemblyName,
                Documentation: documentation,
                BaseType: baseTypeRef,
                Interfaces: interfaces,
                SourceUrl: sourceUrl,
                AppliesTo: context.AppliesTo,
                IsObsolete: isObsolete,
                ObsoleteMessage: obsoleteMessage,
                Attributes: attributes,
                Kind: kind,
                IsReadOnly: type.IsReadOnly,
                IsByRefLike: type.IsRefLikeType,
                Members: MemberBuilder.Build(type, type.Name, uid, context),
                ExtensionBlocks: ExtensionBlockBuilder.Build(type, context));

    /// <summary>
    /// Composes the namespace-qualified, containing-type-qualified
    /// name for a type. Top-level types stay as <c>Namespace.Type</c>;
    /// nested types pick up every outer name on the chain so
    /// <c>Outer.Nested</c> renders as <c>Namespace.Outer.Nested</c>
    /// instead of colliding with a sibling top-level <c>Namespace.Nested</c>.
    /// </summary>
    /// <param name="type">Type whose full name to compute.</param>
    /// <param name="ns">Resolved containing-namespace display string; empty for the global namespace.</param>
    /// <returns>The dotted full name.</returns>
    private static string BuildFullName(INamedTypeSymbol type, string ns)
    {
        if (type.ContainingType is null)
        {
            return ns is [] ? type.Name : $"{ns}.{type.Name}";
        }

        // Walk outwards collecting the containing-type chain so we can
        // emit Namespace.Outer1.Outer2.Type. Stack push order reverses
        // the iteration so the outermost type lands first when popped.
        var chain = new Stack<string>();
        for (var ct = type.ContainingType; ct is not null; ct = ct.ContainingType)
        {
            chain.Push(ct.Name);
        }

        var sb = new System.Text.StringBuilder(ns.Length + (chain.Count * 16) + type.Name.Length);
        if (ns is [_, ..])
        {
            sb.Append(ns).Append('.');
        }

        while (chain.TryPop(out var part))
        {
            sb.Append(part).Append('.');
        }

        sb.Append(type.Name);
        return sb.ToString();
    }
}
