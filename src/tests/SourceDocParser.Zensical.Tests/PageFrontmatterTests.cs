// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins <see cref="PageFrontmatter"/>: every type-kind label, every
/// member-kind label, the package-tag-omitted shortcut, and the
/// obsolete-tag opt-in.
/// </summary>
public class PageFrontmatterTests
{
    /// <summary>Each <see cref="ApiObjectKind"/> maps to its expected kind label.</summary>
    /// <param name="kind">The object kind under test.</param>
    /// <param name="expected">The expected label.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(ApiObjectKind.Class, "kind/class")]
    [Arguments(ApiObjectKind.Struct, "kind/struct")]
    [Arguments(ApiObjectKind.Interface, "kind/interface")]
    [Arguments(ApiObjectKind.Record, "kind/record")]
    [Arguments(ApiObjectKind.RecordStruct, "kind/record-struct")]
    public async Task ForTypeMapsObjectKinds(ApiObjectKind kind, string expected)
    {
        var type = TestData.ObjectType("Foo", kind: kind);

        var fm = PageFrontmatter.ForType(type, ZensicalEmitterOptions.Default);

        await Assert.That(fm).Contains(expected);
    }

    /// <summary>Enum types render <c>kind/enum</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ForTypeMapsEnumKind()
    {
        var fm = PageFrontmatter.ForType(TestData.EnumType("E:Foo"), ZensicalEmitterOptions.Default);

        await Assert.That(fm).Contains("kind/enum");
    }

    /// <summary>Delegate types render <c>kind/delegate</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ForTypeMapsDelegateKind()
    {
        var fm = PageFrontmatter.ForType(TestData.DelegateType("T:Foo"), ZensicalEmitterOptions.Default);

        await Assert.That(fm).Contains("kind/delegate");
    }

    /// <summary>An enum type emits a hidden mkdocs-autorefs anchor for every declared value's UID.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ForTypeEmitsAnchorPerEnumValueUid()
    {
        var baseEnum = TestData.EnumType("T:Foo.Day");
        var enumType = baseEnum with
        {
            Values =
            [
                new("Friday", "F:Foo.Day.Friday", "5", ApiDocumentation.Empty, SourceUrl: null),
                new("Saturday", "F:Foo.Day.Saturday", "6", ApiDocumentation.Empty, SourceUrl: null),
            ],
        };

        var fm = PageFrontmatter.ForType(enumType, ZensicalEmitterOptions.Default);

        await Assert.That(fm).Contains("[](){#F:Foo.Day.Friday}");
        await Assert.That(fm).Contains("[](){#F:Foo.Day.Saturday}");
    }

    /// <summary>The package tag is emitted when routing renames the package.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ForTypeEmitsPackageTagWhenRoutingDiffersFromAssembly()
    {
        var options = new ZensicalEmitterOptions(
        [
            new(FolderName: "ReactiveUI", AssemblyPrefix: "ReactiveUI"),
        ]);
        var type = TestData.ObjectType("Foo", assemblyName: "ReactiveUI.Wpf");

        var fm = PageFrontmatter.ForType(type, options);

        await Assert.That(fm).Contains("assembly/ReactiveUI.Wpf");
        await Assert.That(fm).Contains("package/ReactiveUI");
    }

    /// <summary>The package tag is omitted when it would equal the assembly tag.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ForTypeOmitsPackageTagWhenSameAsAssembly()
    {
        var type = TestData.ObjectType("Foo", assemblyName: "Splat");

        var fm = PageFrontmatter.ForType(type, ZensicalEmitterOptions.Default);

        await Assert.That(fm).Contains("assembly/Splat");
        await Assert.That(fm).DoesNotContain("package/");
    }

    /// <summary>An obsolete type adds the <c>obsolete</c> tag.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ForTypeIncludesObsoleteTagWhenObsolete()
    {
        var type = TestData.ObjectType("Foo") with { IsObsolete = true };

        var fm = PageFrontmatter.ForType(type, ZensicalEmitterOptions.Default);

        await Assert.That(fm).Contains("- obsolete");
    }

    /// <summary>Each <see cref="ApiMemberKind"/> maps to its expected kind label.</summary>
    /// <param name="kind">The member kind under test.</param>
    /// <param name="expected">The expected label.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(ApiMemberKind.Constructor, "kind/constructor")]
    [Arguments(ApiMemberKind.Property, "kind/property")]
    [Arguments(ApiMemberKind.Field, "kind/field")]
    [Arguments(ApiMemberKind.Method, "kind/method")]
    [Arguments(ApiMemberKind.Operator, "kind/operator")]
    [Arguments(ApiMemberKind.Event, "kind/event")]
    [Arguments(ApiMemberKind.EnumValue, "kind/enum-value")]
    public async Task ForMemberMapsMemberKinds(ApiMemberKind kind, string expected)
    {
        var type = TestData.ObjectType("Foo");
        var member = MakeMember(type, kind);

        var fm = PageFrontmatter.ForMember(type, member, [member], ZensicalEmitterOptions.Default);

        await Assert.That(fm).Contains(expected);
    }

    /// <summary>A member in a global-namespace type renders the <c>(global)</c> namespace tag.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ForMemberFallsBackToGlobalNamespaceTag()
    {
        var type = TestData.ObjectType("Foo") with { Namespace = string.Empty };
        var member = MakeMember(type, ApiMemberKind.Method);

        var fm = PageFrontmatter.ForMember(type, member, [member], ZensicalEmitterOptions.Default);

        await Assert.That(fm).Contains("namespace/(global)");
    }

    /// <summary>Builds a minimal <see cref="ApiMember"/> for the supplied <paramref name="type"/> and <paramref name="kind"/>.</summary>
    /// <param name="type">Declaring type.</param>
    /// <param name="kind">Member kind.</param>
    /// <returns>A populated <see cref="ApiMember"/>.</returns>
    private static ApiMember MakeMember(ApiType type, ApiMemberKind kind) =>
        new(
            Name: "Bar",
            Uid: "M:Foo.Bar",
            Kind: kind,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "void Bar()",
            Parameters: [],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: type.Uid,
            ContainingTypeName: type.Name,
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);
}
