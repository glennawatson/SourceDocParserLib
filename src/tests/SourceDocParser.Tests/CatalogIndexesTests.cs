// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Tests;

/// <summary>
/// Direct coverage of <see cref="CatalogIndexes"/> -- the shared
/// algorithm the Zensical and Docfx wrappers delegate to. Pins the
/// empty-singleton path, derived / extension / inherited bucket
/// shapes, the compiler-generated filter, the supplied
/// System.Object baseline format, and argument validation.
/// </summary>
public class CatalogIndexesTests
{
    /// <summary>Stand-in System.Object baseline used when the test doesn't care about its content.</summary>
    private static readonly string[] _emptyBaseline = [];

    /// <summary>An empty type array short-circuits to the static <see cref="CatalogIndexes.Empty"/> singleton.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildEmptyReturnsSingleton()
    {
        var indexes = CatalogIndexes.Build([], _emptyBaseline);

        await Assert.That(indexes).IsSameReferenceAs(CatalogIndexes.Empty);
    }

    /// <summary>Lookup misses on the empty bundle return shared empty arrays.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyBundleReturnsEmptyArraysOnMiss()
    {
        var indexes = CatalogIndexes.Empty;

        await Assert.That(indexes.GetDerived("T:Missing")).IsEmpty();
        await Assert.That(indexes.GetExtensions("T:Missing")).IsEmpty();
        await Assert.That(indexes.GetInherited("T:Missing")).IsEmpty();
    }

    /// <summary>Each subclass is bucketed under its base type UID, in encounter order.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DerivedLookupBucketsBySubclassByBaseUid()
    {
        var baseType = TestData.ObjectType("Base");
        var subA = TestData.ObjectType("DerivedA") with { BaseType = new("Base", "Base") };
        var subB = TestData.ObjectType("DerivedB") with { BaseType = new("Base", "Base") };

        var indexes = CatalogIndexes.Build([baseType, subA, subB], _emptyBaseline);
        var derived = indexes.GetDerived("Base");

        await Assert.That(derived.Length).IsEqualTo(2);
        await Assert.That(derived[0].Uid).IsEqualTo("DerivedA");
        await Assert.That(derived[1].Uid).IsEqualTo("DerivedB");
    }

    /// <summary>Types whose names are compiler-generated (angle-bracketed) are skipped from every index.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CompilerGeneratedTypesAreSkipped()
    {
        var baseType = TestData.ObjectType("Base");
        var displayClass = TestData.ObjectType("<>c__DisplayClass") with { BaseType = new("Base", "Base") };
        var realDerived = TestData.ObjectType("Real") with { BaseType = new("Base", "Base") };

        var indexes = CatalogIndexes.Build([baseType, displayClass, realDerived], _emptyBaseline);
        var derived = indexes.GetDerived("Base");

        await Assert.That(derived.Length).IsEqualTo(1);
        await Assert.That(derived[0].Uid).IsEqualTo("Real");
    }

    /// <summary>Extension methods on a static type are bucketed under the first parameter's type UID.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtensionLookupBucketsByExtendedTypeUid()
    {
        var receiver = TestData.ObjectType("T:Foo.Receiver");
        var staticHost = TestData.ObjectType("T:Foo.Extensions", ApiObjectKind.Class) with
        {
            IsStatic = true,
            Members =
            [
                BuildExtensionMember("Bar", receiverUid: "T:Foo.Receiver"),
                BuildExtensionMember("Baz", receiverUid: "T:Foo.Receiver"),
            ],
        };

        var indexes = CatalogIndexes.Build([receiver, staticHost], _emptyBaseline);
        var extensions = indexes.GetExtensions("T:Foo.Receiver");

        await Assert.That(extensions.Length).IsEqualTo(2);
        await Assert.That(extensions[0].Name).IsEqualTo("Bar");
        await Assert.That(extensions[1].Name).IsEqualTo("Baz");
    }

    /// <summary>Non-static host types contribute no extensions even when their members claim <see cref="ApiMember.IsExtension"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtensionLookupSkipsNonStaticHosts()
    {
        var receiver = TestData.ObjectType("T:Foo.Receiver");
        var instanceHost = TestData.ObjectType("T:Foo.NotStatic", ApiObjectKind.Class) with
        {
            IsStatic = false,
            Members = [BuildExtensionMember("Bar", receiverUid: "T:Foo.Receiver")],
        };

        var indexes = CatalogIndexes.Build([receiver, instanceHost], _emptyBaseline);

        await Assert.That(indexes.GetExtensions("T:Foo.Receiver")).IsEmpty();
    }

    /// <summary>Inherited list folds in the immediate-base members when the base lives in the walked set, then appends the supplied baseline.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InheritedLookupFoldsBaseMembersThenBaseline()
    {
        var baseType = TestData.ObjectType("T:Base") with
        {
            Members = [BuildMethod("M:Base.Inherited"), BuildMethod("M:Base.AlsoInherited")],
        };
        var derived = TestData.ObjectType("T:Derived") with { BaseType = new("Base", "T:Base") };
        string[] baseline = ["M:System.Object.ToString"];

        var indexes = CatalogIndexes.Build([baseType, derived], baseline);
        var inherited = indexes.GetInherited("T:Derived");

        await Assert.That(inherited.Length).IsEqualTo(3);
        await Assert.That(inherited[0]).IsEqualTo("M:Base.Inherited");
        await Assert.That(inherited[1]).IsEqualTo("M:Base.AlsoInherited");
        await Assert.That(inherited[2]).IsEqualTo("M:System.Object.ToString");
    }

    /// <summary>Inherited list still surfaces the baseline when the immediate base isn't walked.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InheritedLookupAppendsBaselineEvenWithoutBase()
    {
        var orphan = TestData.ObjectType("T:Orphan");
        string[] baseline = ["M:System.Object.GetHashCode", "M:System.Object.ToString"];

        var indexes = CatalogIndexes.Build([orphan], baseline);
        var inherited = indexes.GetInherited("T:Orphan");

        await Assert.That(inherited).IsEquivalentTo(baseline);
    }

    /// <summary>Non-class / non-record kinds (interface, struct, enum, delegate, union) don't carry inherited entries.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InheritedLookupSkipsNonClassKinds()
    {
        var iface = TestData.ObjectType("T:IFoo", ApiObjectKind.Interface);
        var @struct = TestData.ObjectType("T:Bar", ApiObjectKind.Struct);
        string[] baseline = ["M:System.Object.ToString"];

        var indexes = CatalogIndexes.Build([iface, @struct], baseline);

        await Assert.That(indexes.GetInherited("T:IFoo")).IsEmpty();
        await Assert.That(indexes.GetInherited("T:Bar")).IsEmpty();
    }

    /// <summary>Records receive the inherited treatment alongside classes.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InheritedLookupIncludesRecordKind()
    {
        var rec = TestData.ObjectType("T:Rec", ApiObjectKind.Record);
        string[] baseline = ["M:System.Object.ToString"];

        var indexes = CatalogIndexes.Build([rec], baseline);

        await Assert.That(indexes.GetInherited("T:Rec")).IsEquivalentTo(baseline);
    }

    /// <summary>Zensical's <c>M:</c>-prefixed baseline flows through verbatim.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InheritedLookupPreservesZensicalBaselineFormat()
    {
        var cls = TestData.ObjectType("T:Foo");
        string[] baseline = ["M:System.Object.ToString", "M:System.Object.GetHashCode"];

        var indexes = CatalogIndexes.Build([cls], baseline);

        await Assert.That(indexes.GetInherited("T:Foo")).IsEquivalentTo(baseline);
    }

    /// <summary>Docfx's bare-name baseline flows through verbatim -- emitter-specific format is just opaque data.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InheritedLookupPreservesDocfxBaselineFormat()
    {
        var cls = TestData.ObjectType("T:Foo");
        string[] baseline = ["System.Object.ToString", "System.Object.GetHashCode"];

        var indexes = CatalogIndexes.Build([cls], baseline);

        await Assert.That(indexes.GetInherited("T:Foo")).IsEquivalentTo(baseline);
    }

    /// <summary>Null arguments are rejected up front.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildRejectsNullArguments()
    {
        await Assert.That(() => CatalogIndexes.Build(null!, _emptyBaseline)).Throws<ArgumentNullException>();
        await Assert.That(() => CatalogIndexes.Build([], null!)).Throws<ArgumentNullException>();
    }

    /// <summary>Builds an extension member whose first parameter targets <paramref name="receiverUid"/>.</summary>
    /// <param name="name">Member name.</param>
    /// <param name="receiverUid">UID of the type being extended.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember BuildExtensionMember(string name, string receiverUid) =>
        new(
            Name: name,
            Uid: $"M:Foo.Extensions.{name}",
            Kind: ApiMemberKind.Method,
            IsStatic: true,
            IsExtension: true,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: $"void {name}()",
            Parameters: [new("self", new(receiverUid, receiverUid), IsOptional: false, IsParams: false, IsIn: false, IsOut: false, IsRef: false, DefaultValue: null)],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: "T:Foo.Extensions",
            ContainingTypeName: "Extensions",
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);

    /// <summary>Builds a plain instance method on a type.</summary>
    /// <param name="uid">Member UID.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember BuildMethod(string uid) =>
        new(
            Name: uid[(uid.LastIndexOf('.') + 1)..],
            Uid: uid,
            Kind: ApiMemberKind.Method,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "void Foo()",
            Parameters: [],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: "T:Base",
            ContainingTypeName: "Base",
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);
}
