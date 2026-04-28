// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.Docfx.Yaml;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins the secondary branches of <see cref="DocfxYamlBuilderExtensions"/>
/// -- the bare-empty-scalar form, seealso emission, qualified-scalar
/// fast paths, and the legacy single-arg <c>AppendTypeItem</c> shim.
/// </summary>
public class DocfxYamlBuilderExtensionsExtraTests
{
    /// <summary>An empty value writes the YAML empty-string sentinel <c>''</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendScalarEmitsEmptyStringSentinelForEmpty()
    {
        var sb = new StringBuilder().AppendScalar(string.Empty);

        await Assert.That(sb.ToString()).IsEqualTo("''");
    }

    /// <summary>An empty seealso array writes nothing.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendSeealsoNoOpsForEmpty()
    {
        var sb = new StringBuilder().AppendSeealso([]);

        await Assert.That(sb.Length).IsEqualTo(0);
    }

    /// <summary>A populated seealso array emits one CRef block per entry, stripping the prefix in altText.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendSeealsoEmitsCRefBlocks()
    {
        var sb = new StringBuilder();

        sb.AppendSeealso(["T:Foo.Bar", "M:Foo.Baz"]);

        var output = sb.ToString();
        await Assert.That(output).Contains("seealso:");
        await Assert.That(output).Contains("    commentId: T:Foo.Bar");
        await Assert.That(output).Contains("    altText: Foo.Bar");
        await Assert.That(output).Contains("    commentId: M:Foo.Baz");
        await Assert.That(output).Contains("    altText: Foo.Baz");
    }

    /// <summary>An empty left-hand side falls through to a plain right-hand scalar.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendQualifiedScalarEmptyLeftEmitsRightOnly()
    {
        var sb = new StringBuilder().AppendQualifiedScalar(string.Empty, '.', "Bar");

        await Assert.That(sb.ToString()).IsEqualTo("Bar");
    }

    /// <summary>An empty right-hand side falls through to a plain left-hand scalar.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendQualifiedScalarEmptyRightEmitsLeftOnly()
    {
        var sb = new StringBuilder().AppendQualifiedScalar("Foo", '.', string.Empty);

        await Assert.That(sb.ToString()).IsEqualTo("Foo");
    }

    /// <summary>A safe pair joins with the separator without quoting.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendQualifiedScalarJoinsSafePairBare()
    {
        var sb = new StringBuilder().AppendQualifiedScalar("Foo", '.', "Bar");

        await Assert.That(sb.ToString()).IsEqualTo("Foo.Bar");
    }

    /// <summary>The legacy single-arg <c>AppendTypeItem</c> overload routes through the empty index.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendTypeItemLegacyOverloadDelegatesToEmptyIndex()
    {
        var sb = new StringBuilder();
        var type = TestData.ObjectType("Foo");

        sb.AppendTypeItem(type);

        var output = sb.ToString();
        await Assert.That(output).Contains("- uid: Foo");
        await Assert.That(output).Contains("commentId: Foo");
        await Assert.That(output).Contains("type: Class");
    }
}
