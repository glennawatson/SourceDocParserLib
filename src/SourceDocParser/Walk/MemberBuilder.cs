// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using SourceDocParser.Model;
using static Microsoft.CodeAnalysis.SymbolDisplayDelegateStyle;
using static Microsoft.CodeAnalysis.SymbolDisplayExtensionMethodStyle;
using static Microsoft.CodeAnalysis.SymbolDisplayGenericsOptions;
using static Microsoft.CodeAnalysis.SymbolDisplayGlobalNamespaceStyle;
using static Microsoft.CodeAnalysis.SymbolDisplayMemberOptions;
using static Microsoft.CodeAnalysis.SymbolDisplayMiscellaneousOptions;
using static Microsoft.CodeAnalysis.SymbolDisplayParameterOptions;
using static Microsoft.CodeAnalysis.SymbolDisplayPropertyStyle;
using static Microsoft.CodeAnalysis.SymbolDisplayTypeQualificationStyle;

namespace SourceDocParser.Walk;

/// <summary>
/// Walks the immediate members of a Roslyn type and converts the
/// externally-visible, classifiable ones to <see cref="ApiMember"/>
/// records. Filters out implicitly-declared symbols (default
/// constructors, accessor methods, backing fields) and unsupported
/// kinds (operators we don't classify, finalizers, etc.). Owns the
/// signature-formatting <see cref="SymbolDisplayFormat"/> the walker
/// uses for the <c>Signature</c> field.
/// </summary>
internal static class MemberBuilder
{
    /// <summary>
    /// Display format for full member signatures including accessibility,
    /// modifiers, parameter names, and default values.
    /// </summary>
    [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1118:Parameter should not span multiple lines", Justification = "Due to line complexity justified")]
    internal static readonly SymbolDisplayFormat SignatureFormat = new(
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
    /// Walks the immediate members of <paramref name="type"/> and
    /// returns the externally-visible, classifiable ones. Skips
    /// compiler-implicit symbols and unsupported kinds.
    /// </summary>
    /// <param name="type">Containing type whose members to collect.</param>
    /// <param name="containingTypeName">Display name of the containing type — propagated into each member.</param>
    /// <param name="containingTypeUid">Roslyn UID of the containing type — propagated into each member.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The documented members.</returns>
    internal static ApiMember[] Build(
        INamedTypeSymbol type,
        string containingTypeName,
        string containingTypeUid,
        SymbolWalkContext context)
    {
        // Pre-size to the raw member count from Roslyn — we'll filter
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

            members.Add(BuildOne(member, kind, containingTypeName, containingTypeUid, context));
        }

        return [.. members];
    }

    /// <summary>
    /// Builds one <see cref="ApiMember"/> from a pre-classified Roslyn
    /// symbol. Hoisted out of <see cref="Build"/> so it can be unit-
    /// tested directly against synthesised symbols, and so the
    /// per-symbol allocation pattern lives in one place.
    /// </summary>
    /// <param name="member">Roslyn symbol to convert.</param>
    /// <param name="kind">Pre-classified member kind (avoids re-running the dispatch).</param>
    /// <param name="containingTypeName">Display name of the containing type.</param>
    /// <param name="containingTypeUid">Roslyn UID of the containing type.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The constructed member.</returns>
    internal static ApiMember BuildOne(
        ISymbol member,
        ApiMemberKind kind,
        string containingTypeName,
        string containingTypeUid,
        SymbolWalkContext context)
    {
        var uid = member.GetDocumentationCommentId() ?? string.Empty;
        var (memberAttributes, memberObsolete, memberObsoleteMessage) = AttributeExtractor.ExtractAll(member);
        return new ApiMember(
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
            Signature: member.ToDisplayString(SignatureFormat),
            Parameters: SymbolWalkerHelpers.BuildParameters(member, context.TypeRefs),
            TypeParameters: SymbolWalkerHelpers.BuildTypeParameters(member),
            ReturnType: SymbolWalkerHelpers.BuildReturnTypeReference(member, context.TypeRefs),
            ContainingTypeUid: containingTypeUid,
            ContainingTypeName: containingTypeName,
            SourceUrl: context.SourceLinks.Resolve(member),
            Documentation: context.Docs.Resolve(member),
            IsObsolete: memberObsolete,
            ObsoleteMessage: memberObsoleteMessage,
            Attributes: memberAttributes);
    }
}
