// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using Microsoft.CodeAnalysis;
using SourceDocParser.Model;

namespace SourceDocParser.Walk;

/// <summary>
/// Pure leaf helpers used by <see cref="SymbolWalker"/>. Each method
/// takes plain Roslyn symbols (and, where needed, the per-walk
/// <see cref="TypeReferenceCache"/>) and returns an
/// <see cref="ApiType"/>-shaped value — no context, no walker state, no
/// IO. Lives apart from <see cref="SymbolWalker"/> so the orchestration
/// (WalkCore / TryBuildType / BuildMembers) is separable from the
/// per-symbol classification + reference-building code, and so the
/// helpers can be unit-tested in isolation.
/// </summary>
internal static class SymbolWalkerHelpers
{
    /// <summary>
    /// Roslyn display format for base-type/interface labels (short names + generic args).
    /// </summary>
    private static readonly SymbolDisplayFormat _displayFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

    /// <summary>
    /// Maps a Roslyn class/struct-kind type to the corresponding
    /// <see cref="ApiObjectKind"/>. Only meaningful for object-shaped
    /// types — caller is expected to have already filtered out enums,
    /// delegates, and union bases.
    /// </summary>
    /// <param name="type">Type to classify.</param>
    /// <returns>The matching object kind, or <see langword="null"/> if the type is not class- or struct-shaped.</returns>
    public static ApiObjectKind? ClassifyObjectKind(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Class when type.IsRecord => ApiObjectKind.Record,
        TypeKind.Class => ApiObjectKind.Class,
        TypeKind.Struct when type.IsRecord => ApiObjectKind.RecordStruct,
        TypeKind.Struct => ApiObjectKind.Struct,
        TypeKind.Interface => ApiObjectKind.Interface,
        _ => null,
    };

    /// <summary>
    /// Returns <see langword="true"/> if the type is a closed-hierarchy
    /// union base — i.e. it implements
    /// <c>System.Runtime.CompilerServices.IUnion</c>. Roslyn 5.x doesn't
    /// know about C# 15 unions yet so this always returns
    /// <see langword="false"/> in practice today; on future Roslyn the
    /// detection lights up automatically.
    /// </summary>
    /// <remarks>
    /// Once a stable Microsoft.CodeAnalysis.CSharp release ships with
    /// first-class union support (<c>TypeKind.Union</c>,
    /// <c>INamedTypeSymbol.UnionCases</c>, or whatever the final API
    /// shape ends up being on the dotnet/roslyn <c>features/Unions</c>
    /// branch), swap this interface-marker probe for the native API and
    /// drop the same-assembly derivation walk in
    /// <see cref="BuildUnionCases"/>.
    /// </remarks>
    /// <param name="type">Type to inspect.</param>
    /// <returns><see langword="true"/> if the type is a union base.</returns>
    public static bool IsUnion(INamedTypeSymbol type)
    {
        for (var i = 0; i < type.AllInterfaces.Length; i++)
        {
            var iface = type.AllInterfaces[i];
            if (iface is { Name: "IUnion", ContainingNamespace: { Name: "CompilerServices", ContainingNamespace.Name: "Runtime" } } &&
                iface.ContainingNamespace.ContainingNamespace.ContainingNamespace?.Name == "System")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Maps an <see cref="ISymbol"/> to <see cref="ApiMemberKind"/>; returns null for kinds the walker skips.
    /// </summary>
    /// <param name="member">Symbol to classify.</param>
    /// <returns>The classified member kind, or null.</returns>
    public static ApiMemberKind? TryClassifyMember(ISymbol member) => member switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => ApiMemberKind.Constructor,
        IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion } => ApiMemberKind.Operator,
        IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation or MethodKind.DeclareMethod } => ApiMemberKind.Method,
        IPropertySymbol => ApiMemberKind.Property,
        IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => ApiMemberKind.EnumValue,
        IFieldSymbol => ApiMemberKind.Field,
        IEventSymbol => ApiMemberKind.Event,
        _ => null,
    };

    /// <summary>
    /// Documents public, protected, and protected-internal members.
    /// </summary>
    /// <param name="accessibility">Accessibility level to check.</param>
    /// <returns>True if externally visible.</returns>
    public static bool IsExternallyVisible(Accessibility accessibility) => accessibility
        is Accessibility.Public
        or Accessibility.Protected
        or Accessibility.ProtectedOrInternal;

    /// <summary>
    /// Returns true when <paramref name="type"/> is one of the
    /// synthesized grouping / marker types Roslyn surfaces for a
    /// C# 14 <c>extension(T receiver) { ... }</c> block. The members
    /// declared inside the block are also emitted by the compiler as
    /// classic <c>[Extension]</c> static methods on the parent
    /// container, so the marker type itself is metadata-only noise:
    /// skipping it keeps the walked catalog free of <c>&lt;&gt;E__N</c>
    /// pages while losing nothing user-visible (the impl methods
    /// already arrive via the parent's member list).
    /// </summary>
    /// <param name="type">Candidate type to inspect.</param>
    /// <returns>True when the type is a C# 14 extension marker.</returns>
    public static bool IsExtensionDeclaration(INamedTypeSymbol type) =>
        type.IsExtension;

    /// <summary>
    /// Returns true when the member carries the C# 11 <c>required</c> modifier.
    /// </summary>
    /// <param name="member">Member to check.</param>
    /// <returns>True if required.</returns>
    public static bool IsRequiredMember(ISymbol member) => member switch
    {
        IPropertySymbol p => p.IsRequired,
        IFieldSymbol f => f.IsRequired,
        _ => false,
    };

    /// <summary>
    /// Renders a default-value literal as C# source text.
    /// </summary>
    /// <param name="value">The literal value.</param>
    /// <returns>The formatted literal string.</returns>
    public static string FormatLiteral(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        char c => $"'{c}'",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
    };

    /// <summary>
    /// Cache-miss factory for <see cref="TypeReferenceCache.GetOrAdd"/>.
    /// </summary>
    /// <param name="symbol">Type symbol the cache is building a reference for.</param>
    /// <returns>A new API type reference.</returns>
    public static ApiTypeReference BuildReference(ITypeSymbol symbol) =>
        new(symbol.ToDisplayString(_displayFormat), symbol.GetDocumentationCommentId() ?? string.Empty);

    /// <summary>
    /// Builds the base-type reference, filtering out noise base types
    /// (<see cref="object"/>, <see cref="ValueType"/>, <see cref="Enum"/>,
    /// <see cref="Delegate"/>, <see cref="MulticastDelegate"/>) since these
    /// add nothing to API documentation.
    /// </summary>
    /// <param name="type">Type whose base to inspect.</param>
    /// <param name="cache">Type reference cache.</param>
    /// <returns>The base type reference, or null if filtered.</returns>
    public static ApiTypeReference? BuildBaseTypeReference(INamedTypeSymbol type, TypeReferenceCache cache)
    {
        if (type.BaseType is not { } baseType)
        {
            return null;
        }

        return baseType.SpecialType is SpecialType.System_Object
            or SpecialType.System_ValueType
            or SpecialType.System_Enum
            or SpecialType.System_MulticastDelegate
            or SpecialType.System_Delegate ? null : cache.GetOrAdd(baseType, BuildReference);
    }

    /// <summary>
    /// Builds references for the interfaces a type directly declares.
    /// Inherited interfaces are omitted to focus on what the type itself adds.
    /// </summary>
    /// <param name="type">Type whose declared interfaces to inspect.</param>
    /// <param name="cache">Type reference cache.</param>
    /// <returns>The declared interface references.</returns>
    public static ApiTypeReference[] BuildInterfaceReferences(INamedTypeSymbol type, TypeReferenceCache cache)
    {
        if (type.Interfaces.IsEmpty)
        {
            return [];
        }

        var refs = new ApiTypeReference[type.Interfaces.Length];
        for (var i = 0; i < type.Interfaces.Length; i++)
        {
            var iface = type.Interfaces[i];
            refs[i] = cache.GetOrAdd(iface, BuildReference);
        }

        return refs;
    }

    /// <summary>
    /// Returns the case types of a C# 15+ closed-hierarchy union: every
    /// other type in the same assembly that derives directly from this
    /// one. Returns an empty list for non-unions or until Roslyn surfaces
    /// the union marker — the caller is responsible for the
    /// <see cref="IsUnion"/> check before invoking this.
    /// </summary>
    /// <param name="type">Union base to inspect.</param>
    /// <param name="cache">Type-reference cache.</param>
    /// <returns>The union case type references.</returns>
    public static ApiTypeReference[] BuildUnionCases(INamedTypeSymbol type, TypeReferenceCache cache)
    {
        // Walk every named type in the same assembly looking for direct
        // derivations of this base. Same-assembly is the closure rule
        // from the closed-hierarchies proposal — case types must live
        // alongside the base.
        var assembly = type.ContainingAssembly;
        if (assembly is null)
        {
            return [];
        }

        var cases = new List<ApiTypeReference>();
        var pending = new Stack<INamespaceSymbol>();
        pending.Push(assembly.GlobalNamespace);
        while (pending.TryPop(out var ns))
        {
            foreach (var nestedNamespace in ns.GetNamespaceMembers())
            {
                pending.Push(nestedNamespace);
            }

            var candidates = ns.GetTypeMembers();
            for (var i = 0; i < candidates.Length; i++)
            {
                CollectCasesRecursive(candidates[i], type, cases, cache);
            }
        }

        return [.. cases];
    }

    /// <summary>
    /// Builds the structured value list for an enum type. Pulls every
    /// public field off the type, materialises the constant value as a
    /// string, and resolves docs + source link per value.
    /// </summary>
    /// <param name="type">Enum type symbol.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The declared values, in source order.</returns>
    public static ApiEnumValue[] BuildEnumValues(INamedTypeSymbol type, SymbolWalkContext context)
    {
        var members = type.GetMembers();
        var values = new List<ApiEnumValue>(members.Length);
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (member is not IFieldSymbol { IsConst: true, IsImplicitlyDeclared: false } field)
            {
                continue;
            }

            if (!IsExternallyVisible(field.DeclaredAccessibility))
            {
                continue;
            }

            values.Add(new(
                Name: field.Name,
                Uid: field.GetDocumentationCommentId() ?? string.Empty,
                Value: FormatLiteral(field.ConstantValue),
                Documentation: context.Docs.Resolve(field),
                SourceUrl: context.SourceLinks.Resolve(field)));
        }

        return [.. values];
    }

    /// <summary>
    /// Builds the structured Invoke signature for a delegate type. Reads
    /// the synthetic <c>Invoke</c> method off the delegate symbol and
    /// captures its return type, parameters, and any generic type
    /// parameters declared on the delegate itself.
    /// </summary>
    /// <param name="type">Delegate type symbol.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The delegate's invoke signature.</returns>
    public static ApiDelegateSignature BuildDelegateInvoke(INamedTypeSymbol type, SymbolWalkContext context)
    {
        var invoke = type.DelegateInvokeMethod;
        var typeParameters = new string[type.TypeParameters.Length];
        for (var i = 0; i < type.TypeParameters.Length; i++)
        {
            typeParameters[i] = type.TypeParameters[i].Name;
        }

        if (invoke is null)
        {
            return new(type.ToDisplayString(), null, [], typeParameters);
        }

        return new(
            Signature: invoke.ToDisplayString(),
            ReturnType: BuildReturnTypeReference(invoke, context.TypeRefs),
            Parameters: BuildParameters(invoke, context.TypeRefs),
            TypeParameters: typeParameters);
    }

    /// <summary>
    /// Returns the parameters of a method, constructor, operator, or indexer.
    /// </summary>
    /// <param name="member">Member whose parameters to read.</param>
    /// <param name="typeRefs">Type-reference cache.</param>
    /// <returns>The parameters.</returns>
    public static ApiParameter[] BuildParameters(ISymbol member, TypeReferenceCache typeRefs)
    {
        var parameters = member switch
        {
            IMethodSymbol m => m.Parameters,
            IPropertySymbol p => p.Parameters,
            _ => default,
        };

        if (parameters.IsDefaultOrEmpty)
        {
            return [];
        }

        var result = new ApiParameter[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            result[i] = new(
                Name: p.Name,
                Type: typeRefs.GetOrAdd(p.Type, BuildReference),
                IsOptional: p.IsOptional,
                IsParams: p.IsParams,
                IsIn: p.RefKind is RefKind.In or RefKind.RefReadOnlyParameter,
                IsOut: p.RefKind is RefKind.Out,
                IsRef: p.RefKind is RefKind.Ref,
                DefaultValue: p.HasExplicitDefaultValue ? FormatLiteral(p.ExplicitDefaultValue) : null);
        }

        return result;
    }

    /// <summary>
    /// Returns generic type-parameter names for a member.
    /// </summary>
    /// <param name="member">Member to inspect.</param>
    /// <returns>The type parameter names.</returns>
    public static string[] BuildTypeParameters(ISymbol member)
    {
        if (member is not IMethodSymbol { TypeParameters: { Length: > 0 } typeParams })
        {
            return [];
        }

        var result = new string[typeParams.Length];
        for (var i = 0; i < typeParams.Length; i++)
        {
            result[i] = typeParams[i].Name;
        }

        return result;
    }

    /// <summary>
    /// Returns the return type of a method, operator, or property.
    /// </summary>
    /// <param name="member">Member to inspect.</param>
    /// <param name="typeRefs">Type-reference cache.</param>
    /// <returns>The return type reference, or null when the member has no return type.</returns>
    public static ApiTypeReference? BuildReturnTypeReference(ISymbol member, TypeReferenceCache typeRefs) => member switch
    {
        IMethodSymbol { ReturnsVoid: true } => null,
        IMethodSymbol m => typeRefs.GetOrAdd(m.ReturnType, BuildReference),
        IPropertySymbol p => typeRefs.GetOrAdd(p.Type, BuildReference),
        _ => null,
    };

    /// <summary>
    /// Walks <paramref name="candidate"/> (and its nested types) looking
    /// for direct derivations of <paramref name="unionBase"/>; appends
    /// each match into <paramref name="cases"/> as a type reference.
    /// </summary>
    /// <param name="candidate">Type to test as a possible case.</param>
    /// <param name="unionBase">Union base type the cases derive from.</param>
    /// <param name="cases">Accumulator the matches are appended to.</param>
    /// <param name="cache">Type-reference cache.</param>
    private static void CollectCasesRecursive(
        INamedTypeSymbol candidate,
        INamedTypeSymbol unionBase,
        List<ApiTypeReference> cases,
        TypeReferenceCache cache)
    {
        if (SymbolEqualityComparer.Default.Equals(candidate.BaseType, unionBase))
        {
            cases.Add(cache.GetOrAdd(candidate, BuildReference));
        }

        var nestedTypes = candidate.GetTypeMembers();
        for (var i = 0; i < nestedTypes.Length; i++)
        {
            CollectCasesRecursive(nestedTypes[i], unionBase, cases, cache);
        }
    }
}
