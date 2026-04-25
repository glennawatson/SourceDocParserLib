// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace SourceDocParser;

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
    /// Maps Roslyn TypeKind to <see cref="ApiTypeKind"/>; returns null for kinds the walker skips.
    /// </summary>
    /// <param name="type">Type to classify.</param>
    /// <returns>The classified API type kind, or null if skipped.</returns>
    public static ApiTypeKind? ClassifyType(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Class when type.IsRecord => ApiTypeKind.Record,
        TypeKind.Class => ApiTypeKind.Class,
        TypeKind.Struct when type.IsRecord => ApiTypeKind.RecordStruct,
        TypeKind.Struct => ApiTypeKind.Struct,
        TypeKind.Interface => ApiTypeKind.Interface,
        TypeKind.Enum => ApiTypeKind.Enum,
        TypeKind.Delegate => ApiTypeKind.Delegate,
        _ => null,
    };

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
    /// <returns>The list of declared interface references.</returns>
    public static List<ApiTypeReference> BuildInterfaceReferences(INamedTypeSymbol type, TypeReferenceCache cache)
    {
        if (type.Interfaces.IsEmpty)
        {
            return [];
        }

        var refs = new List<ApiTypeReference>(type.Interfaces.Length);
        var interfaces = type.Interfaces;
        for (var i = 0; i < interfaces.Length; i++)
        {
            refs.Add(cache.GetOrAdd(interfaces[i], BuildReference));
        }

        return refs;
    }

    /// <summary>
    /// Returns the case types of a C# 15+ union, or an empty list for non-unions.
    /// Stub implementation until Roslyn exposes union-case discovery.
    /// </summary>
    /// <param name="type">Type to inspect.</param>
    /// <returns>The list of union case type references.</returns>
    public static List<ApiTypeReference> BuildUnionCases(INamedTypeSymbol type)
    {
        // Future shape (commented for the Roslyn 6.x bump):
        //   if (type.TypeKind is not TypeKind.Union) return [];
        //   var cases = new List<ApiTypeReference>(type.UnionCases.Length);
        //   foreach (var caseType in type.UnionCases) cases.Add(ToReference(caseType));
        //   return cases;
        _ = type;
        return [];
    }

    /// <summary>
    /// Returns the parameters of a method, constructor, operator, or indexer.
    /// </summary>
    /// <param name="member">Member whose parameters to read.</param>
    /// <param name="typeRefs">Type-reference cache.</param>
    /// <returns>The list of parameters.</returns>
    public static List<ApiParameter> BuildParameters(ISymbol member, TypeReferenceCache typeRefs)
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

        var result = new List<ApiParameter>(parameters.Length);
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            result.Add(new(
                Name: p.Name,
                Type: typeRefs.GetOrAdd(p.Type, BuildReference),
                IsOptional: p.IsOptional,
                IsParams: p.IsParams,
                IsIn: p.RefKind is RefKind.In or RefKind.RefReadOnlyParameter,
                IsOut: p.RefKind is RefKind.Out,
                IsRef: p.RefKind is RefKind.Ref,
                DefaultValue: p.HasExplicitDefaultValue ? FormatLiteral(p.ExplicitDefaultValue) : null));
        }

        return result;
    }

    /// <summary>
    /// Returns generic type-parameter names for a member.
    /// </summary>
    /// <param name="member">Member to inspect.</param>
    /// <returns>The list of type parameter names.</returns>
    public static List<string> BuildTypeParameters(ISymbol member)
    {
        if (member is not IMethodSymbol { TypeParameters: { Length: > 0 } typeParams })
        {
            return [];
        }

        var result = new List<string>(typeParams.Length);
        for (var i = 0; i < typeParams.Length; i++)
        {
            result.Add(typeParams[i].Name);
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
}
