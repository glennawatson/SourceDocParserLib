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

        var input = new TypeBuildContext(
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
            context);

        return type.TypeKind switch
        {
            // Closed-hierarchy unions (C# 15+) are class-shaped and have to
            // be checked before the generic class branch — otherwise we'd
            // emit them as a plain class and lose the case list.
            TypeKind.Class when SymbolWalkerHelpers.IsUnion(type) => BuildUnion(input),
            TypeKind.Enum => BuildEnum(input),
            TypeKind.Delegate => BuildDelegate(input),
            _ => BuildObject(input),
        };
    }

    /// <summary>Constructs the <see cref="ApiUnionType"/> branch.</summary>
    /// <param name="input">Per-type build inputs.</param>
    /// <returns>The constructed union type.</returns>
    internal static ApiUnionType BuildUnion(in TypeBuildContext input) =>
        WithBaseFields(
            ApiUnionType.Empty with
            {
                Members = MemberBuilder.Build(input.Type, input.Type.Name, input.Uid, input.Context),
                Cases = SymbolWalkerHelpers.BuildUnionCases(input.Type, input.Context.TypeRefs),
            },
            input);

    /// <summary>Constructs the <see cref="ApiEnumType"/> branch.</summary>
    /// <param name="input">Per-type build inputs.</param>
    /// <returns>The constructed enum type.</returns>
    internal static ApiEnumType BuildEnum(in TypeBuildContext input) =>
        WithBaseFields(
            ApiEnumType.Empty with
            {
                UnderlyingType = input.Context.TypeRefs.GetOrAdd(
                    input.Type.EnumUnderlyingType ?? input.Type,
                    SymbolWalkerHelpers.BuildReference),
                Values = SymbolWalkerHelpers.BuildEnumValues(input.Type, input.Context),
            },
            input);

    /// <summary>Constructs the <see cref="ApiDelegateType"/> branch.</summary>
    /// <param name="input">Per-type build inputs.</param>
    /// <returns>The constructed delegate type.</returns>
    internal static ApiDelegateType BuildDelegate(in TypeBuildContext input) =>
        WithBaseFields(
            ApiDelegateType.Empty with
            {
                Invoke = SymbolWalkerHelpers.BuildDelegateInvoke(input.Type, input.Context),
            },
            input);

    /// <summary>Constructs the <see cref="ApiObjectType"/> branch — class / struct / interface / record / record struct.</summary>
    /// <param name="input">Per-type build inputs.</param>
    /// <returns>The constructed object type, or null when no <see cref="ApiObjectKind"/> matches (e.g. error symbols).</returns>
    internal static ApiObjectType? BuildObject(in TypeBuildContext input) =>
        SymbolWalkerHelpers.ClassifyObjectKind(input.Type) is not { } kind
            ? null
            : WithBaseFields(
                ApiObjectType.Empty with
                {
                    Kind = kind,
                    IsReadOnly = input.Type.IsReadOnly,
                    IsByRefLike = input.Type.IsRefLikeType,
                    Members = MemberBuilder.Build(input.Type, input.Type.Name, input.Uid, input.Context),
                    ExtensionBlocks = ExtensionBlockBuilder.Build(input.Type, input.Context),
                },
                input);

    /// <summary>
    /// Returns <paramref name="target"/> with every base
    /// <see cref="ApiType"/> field populated from
    /// <paramref name="input"/>. Centralising the 17 base-field
    /// assignments here keeps each <c>Build*</c> branch focused on the
    /// derivation-specific extras and removes the wide block of
    /// duplicated initialisers SonarCloud was flagging across the four
    /// branches.
    /// </summary>
    /// <typeparam name="T">Concrete <see cref="ApiType"/> derivation.</typeparam>
    /// <param name="target">A starting instance — typically the static <c>Empty</c> with derived fields already overridden.</param>
    /// <param name="input">Per-type build inputs.</param>
    /// <returns>The same derivation with base fields filled in.</returns>
    private static T WithBaseFields<T>(T target, in TypeBuildContext input)
        where T : ApiType =>
        target with
        {
            Name = input.Type.Name,
            FullName = input.FullName,
            Uid = input.Uid,
            Namespace = input.Namespace,
            Arity = input.Type.Arity,
            IsStatic = input.Type.IsStatic,
            IsSealed = input.Type.IsSealed,
            IsAbstract = input.Type.IsAbstract,
            AssemblyName = input.Context.AssemblyName,
            Documentation = input.Documentation,
            BaseType = input.BaseTypeRef,
            Interfaces = input.Interfaces,
            SourceUrl = input.SourceUrl,
            AppliesTo = input.Context.AppliesTo,
            IsObsolete = input.IsObsolete,
            ObsoleteMessage = input.ObsoleteMessage,
            Attributes = input.Attributes,
        };

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
