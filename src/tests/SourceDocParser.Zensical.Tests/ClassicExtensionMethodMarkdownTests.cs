// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// End-to-end pin: a classic <c>static void DoIt(this Target self, ...)</c>
/// extension method on a static host should populate
/// <see cref="ZensicalCatalogIndexes.ExtensionMethods"/> for the
/// receiver's UID and the rendered Target page should carry the
/// matching <c>## Extension members</c> markdown section.
/// Mirrors the docfx-side coverage so a regression in either
/// emitter's keying convention surfaces immediately.
/// </summary>
public class ClassicExtensionMethodMarkdownTests
{
    /// <summary>Index lookup for the extended type's UID returns the classic extension method.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IndexLightsUpForExtendedType()
    {
        var (target, helpers) = BuildClassicExtensionFixture();

        var indexes = ZensicalCatalogIndexes.Build([target, helpers]);
        var extensions = indexes.GetExtensions(target.Uid);

        await Assert.That(extensions.Length).IsEqualTo(1);
        await Assert.That(extensions[0].Name).IsEqualTo("DoIt");
    }

    /// <summary>Rendered Markdown for the extended type carries an Extension members section.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtendedTypePageEmitsExtensionMembersSection()
    {
        var (target, helpers) = BuildClassicExtensionFixture();
        var indexes = ZensicalCatalogIndexes.Build([target, helpers]);

        var page = TypePageEmitter.Render(target, ZensicalEmitterOptions.Default, indexes);

        await Assert.That(page).Contains("## Extension members");
        await Assert.That(page).Contains("DoIt");
        await Assert.That(page).Contains("Helpers");
    }

    /// <summary>Builds a Target type and a static Helpers host with one classic extension method targeting Target.</summary>
    /// <returns>The two synthesised types.</returns>
    private static (ApiObjectType Target, ApiObjectType Helpers) BuildClassicExtensionFixture()
    {
        var targetRef = new ApiTypeReference("Target", "T:My.Target");
        var doIt = new ApiMember(
            Name: "DoIt",
            Uid: "M:My.Helpers.DoIt(My.Target,System.Int32)",
            Kind: ApiMemberKind.Method,
            IsStatic: true,
            IsExtension: true,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "public static void DoIt(this Target self, int count)",
            Parameters:
            [
                new("self", targetRef, false, false, false, false, false, null),
                new("count", new("int", "T:System.Int32"), false, false, false, false, false, null),
            ],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: "T:My.Helpers",
            ContainingTypeName: "Helpers",
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);

        var target = TestData.ObjectType("Target") with { Namespace = "My", FullName = "My.Target", Uid = "T:My.Target" };
        var helpers = TestData.ObjectType("Helpers") with
        {
            Namespace = "My",
            FullName = "My.Helpers",
            Uid = "T:My.Helpers",
            IsStatic = true,
            Members = [doIt],
        };
        return (target, helpers);
    }
}
