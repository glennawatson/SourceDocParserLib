// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins the docfx layer's parity additions: compiler-generated symbols
/// are skipped end-to-end (type pages, namespace pages, children, and
/// reference rollups) and the YAML <c>attributes:</c> block applies
/// the same denylist as the Zensical layer.
/// </summary>
public class DocfxAttributeAndCompilerGenTests
{
    /// <summary>Compiler-generated types never reach disk.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitAsyncSkipsCompilerGeneratedTypes()
    {
        using var scratch = new ScratchDirectory();
        var legitimate = TestData.ObjectType("Foo") with { Namespace = "My" };
        var displayClass = TestData.ObjectType("<>c__DisplayClass0_0") with { Namespace = "My" };

        var pages = await new DocfxYamlEmitter().EmitAsync([legitimate, displayClass], scratch.Path);

        // Foo.yml + My.yml namespace page = 2 pages; the display class is dropped.
        await Assert.That(pages).IsEqualTo(2);
        await Assert.That(File.Exists(Path.Combine(scratch.Path, "Foo.yml"))).IsTrue();
    }

    /// <summary>Type page renders an attributes block, dropping CompilerServices markers.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypePageRendersAttributesBlock()
    {
        var type = TestData.ObjectType("Foo") with
        {
            Attributes =
            [
                new("Serializable", "T:System.SerializableAttribute", []),
                new("NullableContext", "T:System.Runtime.CompilerServices.NullableContextAttribute", []),
            ],
        };

        var yaml = DocfxYamlEmitter.Render(type);

        await Assert.That(yaml).Contains("attributes:");
        await Assert.That(yaml).Contains("  - type: System.SerializableAttribute");
        await Assert.That(yaml).DoesNotContain("NullableContext");
    }

    /// <summary>Attribute arguments emit constructor + named values in YAML.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AttributeArgumentsRenderInYaml()
    {
        var type = TestData.ObjectType("Foo") with
        {
            Attributes =
            [
                new(
                    "StyleTypedProperty",
                    "T:System.Windows.StyleTypedPropertyAttribute",
                    [
                        new ApiAttributeArgument(Name: null, Value: "\"ItemContainerStyle\""),
                        new ApiAttributeArgument(Name: "StyleTargetType", Value: "typeof(ListBoxItem)"),
                    ]),
            ],
        };

        var yaml = DocfxYamlEmitter.Render(type);

        await Assert.That(yaml).Contains("    arguments:");
        await Assert.That(yaml).Contains("ItemContainerStyle");
        await Assert.That(yaml).Contains("name: StyleTargetType");
        await Assert.That(yaml).Contains("value: typeof(ListBoxItem)");
    }
}
