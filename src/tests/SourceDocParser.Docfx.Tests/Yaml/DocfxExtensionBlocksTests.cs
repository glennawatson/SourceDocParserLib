// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins the docfx <c>extensionBlocks:</c> emit shape -- one block
/// entry per <see cref="ApiExtensionBlock"/>, each carrying the
/// receiver name + type uid plus the conceptual member uids
/// declared inside the block.
/// </summary>
public class DocfxExtensionBlocksTests
{
    /// <summary>No-op when the type declares no extension blocks.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NoExtensionBlocksEmitsNothing()
    {
        var sb = new StringBuilder();

        sb.AppendExtensionBlocks([]);

        await Assert.That(sb.ToString().Lf()).IsEqualTo(string.Empty);
    }

    /// <summary>One block emits a single entry under <c>extensionBlocks:</c> with the receiver triple + member uid list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SingleBlockEmitsReceiverAndMembers()
    {
        var member = new ApiMember(
            Name: "IsEmpty",
            Uid: "P:Helpers.<>E__0.IsEmpty",
            Kind: ApiMemberKind.Property,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "public bool IsEmpty",
            Parameters: [],
            TypeParameters: [],
            ReturnType: new("bool", "T:System.Boolean"),
            ContainingTypeUid: "T:Helpers.<>E__0",
            ContainingTypeName: "<>E__0",
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);
        ApiExtensionBlock[] blocks =
        [
            new("source", new("string", "T:System.String"), [member]),
        ];

        var sb = new StringBuilder();
        sb.AppendExtensionBlocks(blocks);
        var yaml = sb.ToString().Lf();

        await Assert.That(yaml).Contains("extensionBlocks:");
        await Assert.That(yaml).Contains("  - receiverName: source");
        await Assert.That(yaml).Contains("    receiverType: System.String");
        await Assert.That(yaml).Contains("    members:");
        await Assert.That(yaml).Contains("    - P:Helpers.").Or.Contains("    - Helpers.");
    }

    /// <summary>End-to-end: a static container's extension blocks surface in its rendered YAML page.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypePageEmitsExtensionBlocksField()
    {
        var member = new ApiMember(
            Name: "ToShouty",
            Uid: "M:Helpers.<>E__0.ToShouty",
            Kind: ApiMemberKind.Method,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "public string ToShouty()",
            Parameters: [],
            TypeParameters: [],
            ReturnType: new("string", "T:System.String"),
            ContainingTypeUid: "T:Helpers.<>E__0",
            ContainingTypeName: "<>E__0",
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);
        var helpers = TestData.ObjectType("Helpers") with
        {
            IsStatic = true,
            ExtensionBlocks =
            [
                new("source", new("string", "T:System.String"), [member]),
            ],
        };

        var yaml = DocfxYamlEmitter.Render(helpers).Lf();

        await Assert.That(yaml).Contains("extensionBlocks:");
        await Assert.That(yaml).Contains("  - receiverName: source");
    }
}
