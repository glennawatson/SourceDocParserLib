// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins <see cref="DocfxYamlBuilderExtensions.AppendSyntaxContent"/>
/// — the folded-block path docfx uses to stack attribute usages
/// above a signature inside <c>syntax.content</c>, plus the empty-
/// attributes fast path that preserves the legacy short-scalar form.
/// Per-format tests for the inline attribute usage string itself
/// live in <see cref="AttributeUsageFormatterTests"/>.
/// </summary>
public class DocfxSyntaxContentTests
{
    /// <summary>No surviving attributes ⇒ legacy short-scalar form, byte-identical to the pre-tier-1c output.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyAttributesUsesShortScalarForm()
    {
        var sb = new StringBuilder();

        sb.AppendSyntaxContent([], "public void Run()", indent: "    ");

        await Assert.That(sb.ToString()).IsEqualTo("    content: public void Run()\n");
    }

    /// <summary>Compiler-emitted attributes filtered out via <see cref="DocfxAttributeFilter"/> ⇒ fast path still triggers.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AllFilteredAttributesUsesShortScalarForm()
    {
        var sb = new StringBuilder();
        ApiAttribute[] attrs =
        [
            new("NullableContext", "T:System.Runtime.CompilerServices.NullableContextAttribute", string.Empty, []),
        ];

        sb.AppendSyntaxContent(attrs, "public void Run()", indent: "    ");

        await Assert.That(sb.ToString()).IsEqualTo("    content: public void Run()\n");
    }

    /// <summary>Surviving attributes render as folded-block lines above a blank-separated signature.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SurvivingAttributesRenderAsFoldedBlock()
    {
        var sb = new StringBuilder();
        ApiAttribute[] attrs =
        [
            new("Serializable", "T:System.SerializableAttribute", string.Empty, []),
        ];

        sb.AppendSyntaxContent(attrs, "public class Foo", indent: "    ");

        await Assert.That(sb.ToString()).IsEqualTo("    content: >-\n    [Serializable]\n    \n    public class Foo\n");
    }

    /// <summary>End-to-end: a member with a surviving attribute renders the folded syntax block in its YAML page.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MemberPageEmitsFoldedSyntaxBlockWithAttributePrefix()
    {
        var member = new ApiMember(
            Name: "Run",
            Uid: "M:Foo.Run",
            Kind: ApiMemberKind.Method,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "public void Run()",
            Parameters: [],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: "T:Foo",
            ContainingTypeName: "Foo",
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: [new ApiAttribute("Obsolete", "T:System.ObsoleteAttribute", string.Empty, [])]);
        var type = TestData.ObjectType("Foo") with { Members = [member] };

        var yaml = DocfxYamlEmitter.Render(type);

        await Assert.That(yaml).Contains("    content: >-");
        await Assert.That(yaml).Contains("    [Obsolete]");
        await Assert.That(yaml).Contains("    public void Run()");
    }
}
