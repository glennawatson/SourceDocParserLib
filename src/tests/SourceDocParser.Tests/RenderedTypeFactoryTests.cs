// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="RenderedTypeFactory"/> -- the helper that folds
/// <see cref="XmlDocToMarkdown"/> conversion over an
/// <see cref="ApiType"/> and its members/values. Covers every switch
/// arm (object/union/enum/delegate-default), the no-op short-circuits
/// (member array, value array, type rebuild) and the null-guard
/// argument validation paths.
/// </summary>
public class RenderedTypeFactoryTests
{
    /// <summary>Shared converter -- the conversion logic is exercised in <see cref="XmlDocToMarkdownTests"/>; here we just need a working instance.</summary>
    private readonly XmlDocToMarkdown _converter = new();

    /// <summary>Type with empty docs and no members returns the same instance (renderedDoc is reference-equal so the "no change" arm fires).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderObjectTypeReturnsSameInstanceWhenNothingChanged()
    {
        var input = TestData.ObjectType("T:Foo");

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsSameReferenceAs(input);
    }

    /// <summary>Object type with a renderable summary rebuilds with rendered docs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderObjectTypeRebuildsWhenDocChanges()
    {
        var input = TestData.ObjectType("T:Foo") with
        {
            Documentation = ApiDocumentation.Empty with { Summary = "hello" },
        };

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsNotSameReferenceAs(input);
        await Assert.That(output).IsTypeOf<ApiObjectType>();
        await Assert.That(output.Documentation.Summary).IsEqualTo("hello");
    }

    /// <summary>Object type with a member whose docs render rebuilds the member array.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderObjectTypeRebuildsMembersArrayWhenMemberChanges()
    {
        var member = MakeMember("M:Foo.Bar", summary: "real");
        var unchanged = MakeMember("M:Foo.Baz", summary: string.Empty);
        var input = TestData.ObjectType("T:Foo") with { Members = [unchanged, member] };

        var output = (ApiObjectType)RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsNotSameReferenceAs(input);
        await Assert.That(output.Members).IsNotSameReferenceAs(input.Members);

        // First member is undocumented -- copy preserves the same reference.
        await Assert.That(output.Members[0]).IsSameReferenceAs(unchanged);
        await Assert.That(output.Members[1]).IsNotSameReferenceAs(member);
        await Assert.That(output.Members[1].Documentation.Summary).IsEqualTo("real");
    }

    /// <summary>Union type with empty docs returns the same instance.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderUnionTypeReturnsSameInstanceWhenNothingChanged()
    {
        var input = ApiUnionType.Empty;

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsSameReferenceAs(input);
    }

    /// <summary>Union type with a real summary is rebuilt as an <see cref="ApiUnionType"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderUnionTypeRebuildsWhenDocChanges()
    {
        var input = ApiUnionType.Empty with
        {
            Name = "U",
            Documentation = ApiDocumentation.Empty with { Remarks = "details" },
        };

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsTypeOf<ApiUnionType>();
        await Assert.That(output).IsNotSameReferenceAs(input);
        await Assert.That(output.Documentation.Remarks).IsEqualTo("details");
    }

    /// <summary>Enum type with empty docs and no values returns the same instance.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEnumTypeReturnsSameInstanceWhenNothingChanged()
    {
        var input = TestData.EnumType("T:Day");

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsSameReferenceAs(input);
    }

    /// <summary>Enum type whose value carries renderable docs rebuilds the values array (and preserves the unchanged value's reference).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEnumTypeRebuildsValuesArrayWhenValueChanges()
    {
        var unchanged = new ApiEnumValue("Mon", "F:Day.Mon", "0", ApiDocumentation.Empty, null);
        var changing = new ApiEnumValue(
            Name: "Fri",
            Uid: "F:Day.Fri",
            Value: "4",
            Documentation: ApiDocumentation.Empty with { Summary = "weekend eve" },
            SourceUrl: null);
        var input = TestData.EnumType("T:Day") with { Values = [unchanged, changing] };

        var output = (ApiEnumType)RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsNotSameReferenceAs(input);
        await Assert.That(output.Values).IsNotSameReferenceAs(input.Values);
        await Assert.That(output.Values[0]).IsSameReferenceAs(unchanged);
        await Assert.That(output.Values[1]).IsNotSameReferenceAs(changing);
        await Assert.That(output.Values[1].Documentation.Summary).IsEqualTo("weekend eve");
    }

    /// <summary>Delegate type (the switch's <c>_</c> default arm) with empty docs returns the same instance.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderDelegateTypeReturnsSameInstanceWhenDocUnchanged()
    {
        var input = TestData.DelegateType("T:Handler");

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsSameReferenceAs(input);
    }

    /// <summary>Delegate type with a real summary takes the <c>type with { Documentation = ... }</c> default arm.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderDelegateTypeRebuildsWhenDocChanges()
    {
        var input = TestData.DelegateType("T:Handler") with
        {
            Documentation = ApiDocumentation.Empty with { Summary = "fires on click" },
        };

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsTypeOf<ApiDelegateType>();
        await Assert.That(output).IsNotSameReferenceAs(input);
        await Assert.That(output.Documentation.Summary).IsEqualTo("fires on click");
    }

    /// <summary>Null type argument throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderTypeRejectsNullType() =>
        await Assert.That(() => RenderedTypeFactory.Render((ApiType)null!, _converter))
            .Throws<ArgumentNullException>();

    /// <summary>Null converter argument throws (type overload).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderTypeRejectsNullConverter() =>
        await Assert.That(() => RenderedTypeFactory.Render(TestData.ObjectType("T:X"), null!))
            .Throws<ArgumentNullException>();

    /// <summary>Member with empty docs returns the same instance.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderMemberReturnsSameInstanceWhenDocUnchanged()
    {
        var member = MakeMember("M:Foo.Bar", summary: string.Empty);

        var output = RenderedTypeFactory.Render(member, _converter);

        await Assert.That(output).IsSameReferenceAs(member);
    }

    /// <summary>Member with a renderable summary returns a new record.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderMemberRebuildsWhenDocChanges()
    {
        var member = MakeMember("M:Foo.Bar", summary: "does the thing");

        var output = RenderedTypeFactory.Render(member, _converter);

        await Assert.That(output).IsNotSameReferenceAs(member);
        await Assert.That(output.Documentation.Summary).IsEqualTo("does the thing");
    }

    /// <summary>Null member argument throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderMemberRejectsNullMember() =>
        await Assert.That(() => RenderedTypeFactory.Render((ApiMember)null!, _converter))
            .Throws<ArgumentNullException>();

    /// <summary>Null converter argument throws (member overload).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderMemberRejectsNullConverter() =>
        await Assert.That(() => RenderedTypeFactory.Render(MakeMember("M:Foo.Bar", string.Empty), null!))
            .Throws<ArgumentNullException>();

    /// <summary>Object type with an empty Members array still returns the same instance (the Length-zero short-circuit in RenderMembers).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderObjectTypeWithEmptyMembersHitsZeroLengthShortCircuit()
    {
        var input = TestData.ObjectType("T:Empty");

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsSameReferenceAs(input);
    }

    /// <summary>Enum type with an empty Values array hits the Length-zero short-circuit in RenderValues.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEnumTypeWithEmptyValuesHitsZeroLengthShortCircuit()
    {
        var input = TestData.EnumType("T:Empty");

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsSameReferenceAs(input);
    }

    /// <summary>All members undocumented -- RenderMembers returns the same array reference and RebuildObject returns the same type.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderObjectTypeWithAllUndocumentedMembersReturnsInputUnchanged()
    {
        var m1 = MakeMember("M:Foo.A", summary: string.Empty);
        var m2 = MakeMember("M:Foo.B", summary: string.Empty);
        var input = TestData.ObjectType("T:Foo") with { Members = [m1, m2] };

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsSameReferenceAs(input);
    }

    /// <summary>All enum values undocumented -- RenderValues returns the same array reference and RebuildEnum returns the same type.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEnumTypeWithAllUndocumentedValuesReturnsInputUnchanged()
    {
        var v1 = new ApiEnumValue("A", "F:E.A", "0", ApiDocumentation.Empty, null);
        var v2 = new ApiEnumValue("B", "F:E.B", "1", ApiDocumentation.Empty, null);
        var input = TestData.EnumType("T:E") with { Values = [v1, v2] };

        var output = RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output).IsSameReferenceAs(input);
    }

    /// <summary>Union with members where the first changes -- exercises the path where <c>result</c> is allocated at <c>i = 0</c> (no Array.Copy of preceding entries).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderUnionTypeRebuildsMembersWhenFirstMemberChanges()
    {
        var first = MakeMember("M:U.A", summary: "first");
        var second = MakeMember("M:U.B", summary: string.Empty);
        var input = ApiUnionType.Empty with { Members = [first, second] };

        var output = (ApiUnionType)RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output.Members).IsNotSameReferenceAs(input.Members);
        await Assert.That(output.Members[0]).IsNotSameReferenceAs(first);
        await Assert.That(output.Members[0].Documentation.Summary).IsEqualTo("first");
        await Assert.That(output.Members[1]).IsSameReferenceAs(second);
    }

    /// <summary>Enum where the first value changes -- exercises the <c>i = 0</c> result-allocation path in RenderValues plus the ternary's same-reference branch on later entries.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEnumTypeRebuildsValuesWhenFirstValueChanges()
    {
        var first = new ApiEnumValue(
            "A",
            "F:E.A",
            "0",
            ApiDocumentation.Empty with { Summary = "first" },
            null);
        var second = new ApiEnumValue("B", "F:E.B", "1", ApiDocumentation.Empty, null);
        var input = TestData.EnumType("T:E") with { Values = [first, second] };

        var output = (ApiEnumType)RenderedTypeFactory.Render(input, _converter);

        await Assert.That(output.Values).IsNotSameReferenceAs(input.Values);
        await Assert.That(output.Values[0]).IsNotSameReferenceAs(first);
        await Assert.That(output.Values[0].Documentation.Summary).IsEqualTo("first");
        await Assert.That(output.Values[1]).IsSameReferenceAs(second);
    }

    /// <summary>Builds a deterministic <see cref="ApiMember"/> with the supplied summary text -- empty implies <see cref="ApiDocumentation.Empty"/>.</summary>
    /// <param name="uid">Member UID, also used as Name and signature stem.</param>
    /// <param name="summary">Summary text; empty short-circuits to the shared empty documentation.</param>
    /// <returns>A constructed member.</returns>
    private static ApiMember MakeMember(string uid, string summary)
    {
        var documentation = summary.Length is 0
            ? ApiDocumentation.Empty
            : ApiDocumentation.Empty with { Summary = summary };
        return new(
            Name: uid,
            Uid: uid,
            Kind: ApiMemberKind.Method,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "void " + uid + "()",
            Parameters: [],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: "T:Foo",
            ContainingTypeName: "Foo",
            SourceUrl: null,
            Documentation: documentation,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);
    }
}
