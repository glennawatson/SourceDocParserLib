// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins the docfx <c>attributes:</c> entry on the <c>ctor:</c> field
/// docfx itself emits beneath each <c>type:</c> line. The walker pulls
/// the bound constructor uid from Roslyn so two usages of the same
/// attribute with different argument shapes produce distinct
/// <c>ctor:</c> values.
/// </summary>
public class DocfxAttributeCtorTests
{
    /// <summary>A populated <c>ConstructorUid</c> renders the <c>ctor:</c> line with the M: prefix stripped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorUidRendersAsCtorField()
    {
        var sb = new StringBuilder("attributes:\n");
        var attribute = new ApiAttribute(
            "Browsable",
            "T:System.ComponentModel.BrowsableAttribute",
            "M:System.ComponentModel.BrowsableAttribute.#ctor(System.Boolean)",
            [new ApiAttributeArgument(Name: null, Value: "false")]);

        sb.AppendAttributeEntry(attribute);

        await Assert.That(sb.ToString()).Contains(
            "ctor: System.ComponentModel.BrowsableAttribute.#ctor(System.Boolean)");
    }

    /// <summary>An empty <c>ConstructorUid</c> skips the <c>ctor:</c> line entirely.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyConstructorUidSkipsCtorField()
    {
        var sb = new StringBuilder("attributes:\n");
        var attribute = new ApiAttribute(
            "Serializable",
            "T:System.SerializableAttribute",
            string.Empty,
            []);

        sb.AppendAttributeEntry(attribute);

        await Assert.That(sb.ToString()).DoesNotContain("ctor:");
    }
}
