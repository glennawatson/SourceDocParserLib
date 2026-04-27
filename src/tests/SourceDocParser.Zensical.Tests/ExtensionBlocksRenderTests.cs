// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins the markdown rendering of C# 14 extension blocks on a
/// type page: each <see cref="ApiExtensionBlock"/> renders under
/// <c>### extension(Type receiverName)</c> with its conceptual
/// members listed as autoref bullets.
/// </summary>
public class ExtensionBlocksRenderTests
{
    /// <summary>No-op when the type declares no extension blocks.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NoExtensionBlocksOmitsSection()
    {
        var page = TypePageEmitter.Render(TestData.ObjectType("Foo"));

        await Assert.That(page).DoesNotContain("Extension blocks");
    }

    /// <summary>Type with one extension block renders the heading + receiver + member bullets.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SingleBlockRendersHeadingAndMembers()
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
        var helpers = TestData.ObjectType("Helpers") with
        {
            IsStatic = true,
            ExtensionBlocks =
            [
                new("source", new("string", "T:System.String"), [member]),
            ],
        };

        var page = TypePageEmitter.Render(helpers);

        await Assert.That(page).Contains("## Extension blocks");
        await Assert.That(page).Contains("### extension(");
        await Assert.That(page).Contains("source");
        await Assert.That(page).Contains("`IsEmpty`");
    }
}
