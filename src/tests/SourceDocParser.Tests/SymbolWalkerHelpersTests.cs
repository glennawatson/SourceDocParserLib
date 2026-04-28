// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using NSubstitute;
using SourceDocParser.Model;
using SourceDocParser.Walk;

namespace SourceDocParser.Tests;

/// <summary>
/// Targeted unit tests for the leaf helpers in
/// <see cref="SymbolWalkerHelpers"/>. Roslyn symbols are mocked via
/// NSubstitute so each test exercises one decision branch in isolation --
/// no in-memory <c>CSharpCompilation</c> spin-up required.
/// </summary>
public class SymbolWalkerHelpersTests
{
    /// <summary>
    /// <see cref="SymbolWalkerHelpers.ClassifyObjectKind"/> maps each
    /// Roslyn <see cref="TypeKind"/> to the matching <see cref="ApiObjectKind"/>.
    /// Enums and delegates are classified through dedicated walker
    /// branches (see <see cref="ApiEnumType"/> / <see cref="ApiDelegateType"/>)
    /// rather than through this helper, so they aren't covered here.
    /// </summary>
    /// <param name="kind">Roslyn type kind to feed in.</param>
    /// <param name="isRecord">Whether the type is a record.</param>
    /// <param name="expected">Expected classification.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(TypeKind.Class, false, ApiObjectKind.Class)]
    [Arguments(TypeKind.Class, true, ApiObjectKind.Record)]
    [Arguments(TypeKind.Struct, false, ApiObjectKind.Struct)]
    [Arguments(TypeKind.Struct, true, ApiObjectKind.RecordStruct)]
    [Arguments(TypeKind.Interface, false, ApiObjectKind.Interface)]
    public async Task ClassifyObjectKindMapsKnownKinds(TypeKind kind, bool isRecord, ApiObjectKind expected)
    {
        var symbol = Substitute.For<INamedTypeSymbol>();
        symbol.TypeKind.Returns(kind);
        symbol.IsRecord.Returns(isRecord);

        await Assert.That(SymbolWalkerHelpers.ClassifyObjectKind(symbol)).IsEqualTo(expected);
    }

    /// <summary>Non-object kinds (enums, delegates, modules) classify as null.</summary>
    /// <param name="kind">Roslyn type kind to feed in.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(TypeKind.Enum)]
    [Arguments(TypeKind.Delegate)]
    [Arguments(TypeKind.Module)]
    public async Task ClassifyObjectKindReturnsNullForNonObjectKinds(TypeKind kind)
    {
        var symbol = Substitute.For<INamedTypeSymbol>();
        symbol.TypeKind.Returns(kind);

        await Assert.That(SymbolWalkerHelpers.ClassifyObjectKind(symbol)).IsNull();
    }

    /// <summary>
    /// <see cref="SymbolWalkerHelpers.IsExternallyVisible"/> returns true
    /// only for accessibilities that surface in public docs.
    /// </summary>
    /// <param name="accessibility">The accessibility level to check.</param>
    /// <param name="expected">Expected visibility outcome.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(Accessibility.Public, true)]
    [Arguments(Accessibility.Protected, true)]
    [Arguments(Accessibility.ProtectedOrInternal, true)]
    [Arguments(Accessibility.Internal, false)]
    [Arguments(Accessibility.Private, false)]
    [Arguments(Accessibility.ProtectedAndInternal, false)]
    [Arguments(Accessibility.NotApplicable, false)]
    public async Task IsExternallyVisibleMatchesPolicy(Accessibility accessibility, bool expected) =>
        await Assert.That(SymbolWalkerHelpers.IsExternallyVisible(accessibility)).IsEqualTo(expected);

    /// <summary>
    /// Methods classify as Constructor / Operator / Method depending on <see cref="MethodKind"/>.
    /// </summary>
    /// <param name="methodKind">Roslyn method kind.</param>
    /// <param name="expected">Expected member kind.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(MethodKind.Constructor, ApiMemberKind.Constructor)]
    [Arguments(MethodKind.StaticConstructor, ApiMemberKind.Constructor)]
    [Arguments(MethodKind.UserDefinedOperator, ApiMemberKind.Operator)]
    [Arguments(MethodKind.Conversion, ApiMemberKind.Operator)]
    [Arguments(MethodKind.Ordinary, ApiMemberKind.Method)]
    [Arguments(MethodKind.ExplicitInterfaceImplementation, ApiMemberKind.Method)]
    public async Task TryClassifyMemberMapsMethodKinds(MethodKind methodKind, ApiMemberKind expected)
    {
        var method = Substitute.For<IMethodSymbol>();
        method.MethodKind.Returns(methodKind);

        await Assert.That(SymbolWalkerHelpers.TryClassifyMember(method)).IsEqualTo(expected);
    }

    /// <summary>Properties / events / non-enum fields classify per their interface.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryClassifyMemberMapsPropertyEventField()
    {
        var prop = Substitute.For<IPropertySymbol>();
        var ev = Substitute.For<IEventSymbol>();
        var field = Substitute.For<IFieldSymbol>();
        var containing = Substitute.For<INamedTypeSymbol>();
        containing.TypeKind.Returns(TypeKind.Class);
        field.ContainingType.Returns(containing);

        await Assert.That(SymbolWalkerHelpers.TryClassifyMember(prop)).IsEqualTo(ApiMemberKind.Property);
        await Assert.That(SymbolWalkerHelpers.TryClassifyMember(ev)).IsEqualTo(ApiMemberKind.Event);
        await Assert.That(SymbolWalkerHelpers.TryClassifyMember(field)).IsEqualTo(ApiMemberKind.Field);
    }

    /// <summary>Fields on enum types classify as <see cref="ApiMemberKind.EnumValue"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryClassifyMemberClassifiesEnumFieldsAsEnumValue()
    {
        var enumType = Substitute.For<INamedTypeSymbol>();
        enumType.TypeKind.Returns(TypeKind.Enum);
        var field = Substitute.For<IFieldSymbol>();
        field.ContainingType.Returns(enumType);

        await Assert.That(SymbolWalkerHelpers.TryClassifyMember(field)).IsEqualTo(ApiMemberKind.EnumValue);
    }

    /// <summary>Unsupported member kinds classify as null.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryClassifyMemberReturnsNullForUnsupportedKind()
    {
        var ns = Substitute.For<INamespaceSymbol>();
        await Assert.That(SymbolWalkerHelpers.TryClassifyMember(ns)).IsNull();
    }

    /// <summary>Property/field with the C# 11 <c>required</c> modifier returns true; method returns false.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsRequiredMemberMatchesRequiredFlag()
    {
        var requiredProp = Substitute.For<IPropertySymbol>();
        requiredProp.IsRequired.Returns(true);

        var optionalField = Substitute.For<IFieldSymbol>();
        optionalField.IsRequired.Returns(false);

        var method = Substitute.For<IMethodSymbol>();

        await Assert.That(SymbolWalkerHelpers.IsRequiredMember(requiredProp)).IsTrue();
        await Assert.That(SymbolWalkerHelpers.IsRequiredMember(optionalField)).IsFalse();
        await Assert.That(SymbolWalkerHelpers.IsRequiredMember(method)).IsFalse();
    }

    /// <summary>
    /// Default-value literal renders for null, strings, chars, bools, and
    /// numeric types.
    /// </summary>
    /// <param name="value">Value to render.</param>
    /// <param name="expected">Expected rendered string.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(null, "null")]
    [Arguments("hello", "\"hello\"")]
    [Arguments('a', "'a'")]
    [Arguments(true, "true")]
    [Arguments(false, "false")]
    [Arguments(42, "42")]
    public async Task FormatLiteralRendersExpected(object? value, string expected) =>
        await Assert.That(SymbolWalkerHelpers.FormatLiteral(value)).IsEqualTo(expected);

    /// <summary>
    /// <see cref="SymbolWalkerHelpers.BuildBaseTypeReference"/> filters
    /// the noise base types (object, ValueType, Enum, Delegate,
    /// MulticastDelegate) and returns null for them.
    /// </summary>
    /// <param name="specialType">Special type to feed as base.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(SpecialType.System_Object)]
    [Arguments(SpecialType.System_ValueType)]
    [Arguments(SpecialType.System_Enum)]
    [Arguments(SpecialType.System_MulticastDelegate)]
    [Arguments(SpecialType.System_Delegate)]
    public async Task BuildBaseTypeReferenceFiltersNoiseBases(SpecialType specialType)
    {
        var baseSymbol = Substitute.For<INamedTypeSymbol>();
        baseSymbol.SpecialType.Returns(specialType);
        var type = Substitute.For<INamedTypeSymbol>();
        type.BaseType.Returns(baseSymbol);

        await Assert.That(SymbolWalkerHelpers.BuildBaseTypeReference(type, new())).IsNull();
    }

    /// <summary>Returns null when the type has no base.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildBaseTypeReferenceReturnsNullWhenNoBase()
    {
        var type = Substitute.For<INamedTypeSymbol>();
        type.BaseType.Returns((INamedTypeSymbol?)null);

        await Assert.That(SymbolWalkerHelpers.BuildBaseTypeReference(type, new())).IsNull();
    }

    /// <summary>Empty <c>Interfaces</c> returns an empty list without touching the cache.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildInterfaceReferencesReturnsEmptyForNoInterfaces()
    {
        var type = Substitute.For<INamedTypeSymbol>();
        type.Interfaces.Returns([]);

        var refs = SymbolWalkerHelpers.BuildInterfaceReferences(type, new());

        await Assert.That(refs).IsEmpty();
    }

    /// <summary>
    /// <see cref="SymbolWalkerHelpers.BuildUnionCases"/> walks the
    /// containing assembly looking for direct derivations of the union
    /// base. With a substitute type that has no containing assembly the
    /// helper returns an empty list and never throws.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildUnionCasesReturnsEmptyWhenNoContainingAssembly()
    {
        var type = Substitute.For<INamedTypeSymbol>();
        type.ContainingAssembly.Returns((IAssemblySymbol?)null);
        await Assert.That(SymbolWalkerHelpers.BuildUnionCases(type, new())).IsEmpty();
    }

    /// <summary>
    /// Members without parameters (events, fields) get an empty
    /// parameter list without invoking the type-reference cache.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildParametersReturnsEmptyForNonParameterizedMember()
    {
        var ev = Substitute.For<IEventSymbol>();
        await Assert.That(SymbolWalkerHelpers.BuildParameters(ev, new())).IsEmpty();
    }

    /// <summary>Methods with no parameters yield an empty list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildParametersReturnsEmptyForParameterlessMethod()
    {
        var method = Substitute.For<IMethodSymbol>();
        method.Parameters.Returns([]);

        await Assert.That(SymbolWalkerHelpers.BuildParameters(method, new())).IsEmpty();
    }

    /// <summary>Methods without type parameters yield an empty list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildTypeParametersReturnsEmptyForNonGenericMethod()
    {
        var method = Substitute.For<IMethodSymbol>();
        method.TypeParameters.Returns([]);

        await Assert.That(SymbolWalkerHelpers.BuildTypeParameters(method)).IsEmpty();
    }

    /// <summary>Generic method's type parameters surface in declaration order.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildTypeParametersReturnsNamesForGenericMethod()
    {
        var t1 = Substitute.For<ITypeParameterSymbol>();
        t1.Name.Returns("T");
        var t2 = Substitute.For<ITypeParameterSymbol>();
        t2.Name.Returns("U");

        var method = Substitute.For<IMethodSymbol>();
        method.TypeParameters.Returns([t1, t2]);

        var names = SymbolWalkerHelpers.BuildTypeParameters(method);

        await Assert.That(names).IsEquivalentTo((List<string>)["T", "U"]);
    }

    /// <summary>Void-returning methods yield a null return reference.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildReturnTypeReferenceReturnsNullForVoid()
    {
        var method = Substitute.For<IMethodSymbol>();
        method.ReturnsVoid.Returns(true);

        await Assert.That(SymbolWalkerHelpers.BuildReturnTypeReference(method, new())).IsNull();
    }

    /// <summary>Symbols outside method/property yield a null return reference.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildReturnTypeReferenceReturnsNullForUnsupportedSymbol()
    {
        var ev = Substitute.For<IEventSymbol>();
        await Assert.That(SymbolWalkerHelpers.BuildReturnTypeReference(ev, new())).IsNull();
    }
}
