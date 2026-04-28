// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// End-to-end pin: a classic <c>static void DoIt(this Target self, ...)</c>
/// extension method on a static host should populate
/// <see cref="DocfxCatalogIndexes.GetExtensions(string)"/> for the
/// receiver's UID and the rendered Target.yml page should carry the
/// matching <c>extensionMethods:</c> field. Stops a regression in the
/// keying convention (extended-type UID → method UIDs) the index
/// build relies on.
/// </summary>
public class ClassicExtensionMethodEmitTests
{
    /// <summary>Index lookup for the extended type's UID returns the classic extension method.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IndexLightsUpForExtendedType()
    {
        var (target, helpers) = BuildClassicExtensionFixture();

        var indexes = DocfxCatalogIndexes.Build([target, helpers]);
        var extensions = indexes.GetExtensions(target.Uid);

        await Assert.That(extensions.Length).IsEqualTo(1);
        await Assert.That(extensions[0].Name).IsEqualTo("DoIt");
    }

    /// <summary>Rendered YAML for the extended type carries the <c>extensionMethods:</c> field with the method UID.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtendedTypePageEmitsExtensionMethodsField()
    {
        var (target, helpers) = BuildClassicExtensionFixture();
        var indexes = DocfxCatalogIndexes.Build([target, helpers]);

        var yaml = DocfxYamlEmitter.Render(target, BuildInternalUids(target, helpers), indexes);

        // Docfx convention strips the M: prefix when emitting member
        // UIDs in YAML scalar position (CommentIdPrefix.Strip).
        await Assert.That(yaml).Contains("extensionMethods:");
        await Assert.That(yaml).Contains("- My.Helpers.DoIt");
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

    /// <summary>Builds the internal-uid set the reference enricher uses to classify refs as local.</summary>
    /// <param name="types">Types to register.</param>
    /// <returns>The classifier set.</returns>
    private static HashSet<string> BuildInternalUids(params ApiType[] types)
    {
        var set = new HashSet<string>(types.Length, StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            set.Add(types[i].Uid);
        }

        return set;
    }
}
