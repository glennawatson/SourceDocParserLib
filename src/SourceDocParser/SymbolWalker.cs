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
internal static class SymbolWalker
{
    /// <summary>
    /// Roslyn display format for base-type/interface labels using short names
    /// and generic arguments.
    /// </summary>
    private static readonly SymbolDisplayFormat _displayFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

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
    public static ApiCatalog Walk(string tfm, IAssemblySymbol assembly, Compilation compilation, SourceLinkResolver sourceLinks)
    {
        var typeRefs = new TypeReferenceCache();
        var assemblyName = assembly.Name;

        // Total count isn't known upfront - typical assemblies land in
        // the low hundreds of public types — so let the list grow
        // dynamically rather than picking an arbitrary capacity.
        List<ApiType> types = [];
        var pendingNamespaces = new Stack<INamespaceSymbol>();
        var pendingTypes = new Stack<INamedTypeSymbol>();

        pendingNamespaces.Push(assembly.GlobalNamespace);

        var docs = new DocResolver(compilation);

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
                if (!IsExternallyVisible(type.DeclaredAccessibility))
                {
                    continue;
                }

                if (TryBuildType(type, assemblyName, tfm, docs, typeRefs, sourceLinks) is { } apiType)
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
    /// Builds an ApiType for one Roslyn INamedTypeSymbol. Returns null for types
    /// that cannot be classified (error symbols, modules, etc.).
    /// </summary>
    /// <param name="type">Source type symbol.</param>
    /// <param name="assemblyName">Simple name of the declaring assembly.</param>
    /// <param name="tfm">TFM the declaring assembly was loaded under.</param>
    /// <param name="docs">Per-walk DocResolver for cached XML doc parses.</param>
    /// <param name="typeRefs">Cache of typeref records keyed by symbol.</param>
    /// <param name="sourceLinks">SourceLink resolver for the declaring assembly.</param>
    /// <returns>The generated API type, or null if it could not be built.</returns>
    private static ApiType? TryBuildType(INamedTypeSymbol type, string assemblyName, string tfm, DocResolver docs, TypeReferenceCache typeRefs, SourceLinkResolver sourceLinks)
    {
        if (ClassifyType(type) is not { } kind)
        {
            return null;
        }

        var uid = type.GetDocumentationCommentId() ?? string.Empty;
        var ns = type.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString()
            : string.Empty;
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
            AssemblyName: assemblyName,
            Documentation: docs.Resolve(type),
            BaseType: BuildBaseTypeReference(type, typeRefs),
            Interfaces: BuildInterfaceReferences(type, typeRefs),
            UnionCases: BuildUnionCases(type),
            Members: BuildMembers(type, type.Name, uid, docs, typeRefs, sourceLinks),
            SourceUrl: sourceLinks.Resolve(type),
            AppliesTo: [tfm]);
    }

    /// <summary>
    /// Returns the case types of a C# 15+ union, or an empty list for non-unions.
    /// Stub implementation until Roslyn exposes union-case discovery.
    /// </summary>
    /// <param name="type">Type to inspect.</param>
    /// <returns>The list of union case type references.</returns>
    private static List<ApiTypeReference> BuildUnionCases(INamedTypeSymbol type)
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
    /// Builds the base-type reference, filtering out noise base types like
    /// System.Object or System.ValueType.
    /// </summary>
    /// <param name="type">Type whose base to inspect.</param>
    /// <param name="cache">Type reference cache.</param>
    /// <returns>The base type reference, or null if filtered.</returns>
    private static ApiTypeReference? BuildBaseTypeReference(INamedTypeSymbol type, TypeReferenceCache cache)
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
    private static List<ApiTypeReference> BuildInterfaceReferences(INamedTypeSymbol type, TypeReferenceCache cache)
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
    /// Cache-miss factory for TypeReferenceCache.GetOrAdd.
    /// </summary>
    /// <param name="symbol">Type symbol the cache is building a reference for.</param>
    /// <returns>A new API type reference.</returns>
    private static ApiTypeReference BuildReference(ITypeSymbol symbol) =>
        new(symbol.ToDisplayString(_displayFormat), symbol.GetDocumentationCommentId() ?? string.Empty);

    /// <summary>
    /// Maps Roslyn TypeKind to ApiTypeKind, skipping types that are not surfaced.
    /// </summary>
    /// <param name="type">Type to classify.</param>
    /// <returns>The classified API type kind, or null if skipped.</returns>
    private static ApiTypeKind? ClassifyType(INamedTypeSymbol type) => type.TypeKind switch
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
    /// Walks the immediate members of a type and returns the documented ones.
    /// Skips compiler-generated and non-public members.
    /// </summary>
    /// <param name="type">Containing type.</param>
    /// <param name="containingTypeName">Display name of the containing type.</param>
    /// <param name="containingTypeUid">Roslyn UID of the containing type.</param>
    /// <param name="docs">DocResolver for XML doc lookup.</param>
    /// <param name="typeRefs">Type-reference cache.</param>
    /// <param name="sourceLinks">SourceLink resolver.</param>
    /// <returns>The list of documented members.</returns>
    private static List<ApiMember> BuildMembers(
        INamedTypeSymbol type,
        string containingTypeName,
        string containingTypeUid,
        DocResolver docs,
        TypeReferenceCache typeRefs,
        SourceLinkResolver sourceLinks)
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
            if (member.IsImplicitlyDeclared || !IsExternallyVisible(member.DeclaredAccessibility))
            {
                continue;
            }

            if (TryClassifyMember(member) is not { } kind)
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
                IsRequired: IsRequiredMember(member),
                IsVirtual: member.IsVirtual,
                IsOverride: member.IsOverride,
                IsAbstract: member.IsAbstract,
                IsSealed: member.IsSealed,
                Signature: member.ToDisplayString(_signatureFormat),
                Parameters: BuildParameters(member, typeRefs),
                TypeParameters: BuildTypeParameters(member),
                ReturnType: BuildReturnTypeReference(member, typeRefs),
                ContainingTypeUid: containingTypeUid,
                ContainingTypeName: containingTypeName,
                SourceUrl: sourceLinks.Resolve(member),
                Documentation: docs.Resolve(member)));
        }

        return members;
    }

    /// <summary>
    /// Returns the parameters of a method, constructor, operator, or indexer.
    /// </summary>
    /// <param name="member">Member whose parameters to read.</param>
    /// <param name="typeRefs">Type-reference cache.</param>
    /// <returns>The list of parameters.</returns>
    private static List<ApiParameter> BuildParameters(ISymbol member, TypeReferenceCache typeRefs)
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
    private static List<string> BuildTypeParameters(ISymbol member)
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
    /// <returns>The return type reference, or null.</returns>
    private static ApiTypeReference? BuildReturnTypeReference(ISymbol member, TypeReferenceCache typeRefs) => member switch
    {
        IMethodSymbol { ReturnsVoid: true } => null,
        IMethodSymbol m => typeRefs.GetOrAdd(m.ReturnType, BuildReference),
        IPropertySymbol p => typeRefs.GetOrAdd(p.Type, BuildReference),
        _ => null,
    };

    /// <summary>
    /// Renders a default-value literal as csharp source.
    /// </summary>
    /// <param name="value">The literal value.</param>
    /// <returns>The formatted literal string.</returns>
    private static string FormatLiteral(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        char c => $"'{c}'",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
    };

    /// <summary>
    /// Returns true if the member carries the C# 11 required modifier.
    /// </summary>
    /// <param name="member">Member to check.</param>
    /// <returns>True if required.</returns>
    private static bool IsRequiredMember(ISymbol member) => member switch
    {
        IPropertySymbol p => p.IsRequired,
        IFieldSymbol f => f.IsRequired,
        _ => false,
    };

    /// <summary>
    /// Documents public, protected, and protected-internal members.
    /// </summary>
    /// <param name="accessibility">Accessibility level to check.</param>
    /// <returns>True if externally visible.</returns>
    private static bool IsExternallyVisible(Accessibility accessibility) => accessibility
        is Accessibility.Public
        or Accessibility.Protected
        or Accessibility.ProtectedOrInternal;

    /// <summary>
    /// Maps an ISymbol onto our ApiMemberKind enum.
    /// </summary>
    /// <param name="member">Symbol to classify.</param>
    /// <returns>The classified member kind, or null.</returns>
    private static ApiMemberKind? TryClassifyMember(ISymbol member) => member switch
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
}
