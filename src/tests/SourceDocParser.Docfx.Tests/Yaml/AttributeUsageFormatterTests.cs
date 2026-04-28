// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins <see cref="AttributeUsageFormatter.Render"/> +
/// <see cref="AttributeUsageFormatter.ComputeLength"/>: the
/// <c>Name(arg, Named=val)</c> string the docfx syntax-content prefix
/// carries above each member's signature. Length precompute and the
/// rendered string must agree to the byte -- a mismatch would either
/// truncate output or leave a NUL tail.
/// </summary>
public class AttributeUsageFormatterTests
{
    /// <summary>An attribute with no arguments renders as the bare display name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NoArgumentsRendersBareName()
    {
        var rendered = AttributeUsageFormatter.Render(
            new("Serializable", "T:System.SerializableAttribute", string.Empty, []));

        await Assert.That(rendered).IsEqualTo("Serializable");
    }

    /// <summary>A single positional argument renders inside parens.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SinglePositionalArgumentRenders()
    {
        var rendered = AttributeUsageFormatter.Render(new(
            "Browsable",
            "T:System.ComponentModel.BrowsableAttribute",
            string.Empty,
            [new(Name: null, Value: "false")]));

        await Assert.That(rendered).IsEqualTo("Browsable(false)");
    }

    /// <summary>Mixed positional + named arguments preserve declaration order with comma-space separators and an <c>=</c> sign on named values.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MixedPositionalAndNamedRenders()
    {
        var rendered = AttributeUsageFormatter.Render(new(
            "StyleTypedProperty",
            "T:System.Windows.StyleTypedPropertyAttribute",
            string.Empty,
            [
                new(Name: null, Value: "\"ItemContainerStyle\""),
                new(Name: "StyleTargetType", Value: "typeof(ListBoxItem)"),
            ]));

        await Assert.That(rendered).IsEqualTo("StyleTypedProperty(\"ItemContainerStyle\", StyleTargetType=typeof(ListBoxItem))");
    }

    /// <summary>ComputeLength matches the actual rendered length byte-for-byte.</summary>
    /// <param name="argCount">Number of positional arguments to fabricate.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(10)]
    public async Task ComputeLengthMatchesRenderedLength(int argCount)
    {
        var args = new ApiAttributeArgument[argCount];
        for (var i = 0; i < argCount; i++)
        {
            args[i] = new(Name: i % 2 == 0 ? null : "Named" + i, Value: "v" + i);
        }

        var attribute = new ApiAttribute("Foo", "T:My.FooAttribute", string.Empty, args);

        var rendered = AttributeUsageFormatter.Render(attribute).Lf();

        // Empty-argument fast path returns the bare name; ComputeLength
        // is only consulted on the slow path so we only need to assert
        // length parity when there is at least one argument.
        if (argCount > 0)
        {
            await Assert.That(rendered.Length).IsEqualTo(AttributeUsageFormatter.ComputeLength(attribute));
        }
        else
        {
            await Assert.That(rendered).IsEqualTo("Foo");
        }
    }
}
