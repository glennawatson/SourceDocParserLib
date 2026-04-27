// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins the Zensical-layer attribute filter on the namespace
/// denylist + ExtensionAttribute allowlist semantics, plus the
/// inline-list rendering format <c>`[Foo(arg, Named=val)]`</c>.
/// </summary>
public class AttributeFilterTests
{
    /// <summary>Attributes from <c>System.Runtime.CompilerServices</c> are dropped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DropsCompilerServicesAttributes()
    {
        ApiAttribute[] attrs =
        [
            new("NullableContext", "T:System.Runtime.CompilerServices.NullableContextAttribute", string.Empty, []),
            new("IsReadOnly", "T:System.Runtime.CompilerServices.IsReadOnlyAttribute", string.Empty, []),
        ];

        var rendered = AttributeFilter.RenderInlineList(attrs);

        await Assert.That(rendered).IsEqualTo(string.Empty);
    }

    /// <summary><c>ExtensionAttribute</c> is allowlisted despite being in the denylisted namespace.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task KeepsExtensionAttributeAllowlistEntry()
    {
        ApiAttribute[] attrs =
        [
            new("Extension", "T:System.Runtime.CompilerServices.ExtensionAttribute", string.Empty, []),
        ];

        var rendered = AttributeFilter.RenderInlineList(attrs);

        await Assert.That(rendered).IsEqualTo("`[Extension]`");
    }

    /// <summary>Constructor and named arguments render in source order, joined by commas.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RendersConstructorAndNamedArguments()
    {
        ApiAttribute[] attrs =
        [
            new(
                "StyleTypedProperty",
                "T:System.Windows.StyleTypedPropertyAttribute",
                string.Empty,
                [
                    new ApiAttributeArgument(Name: null, Value: "\"ItemContainerStyle\""),
                    new ApiAttributeArgument(Name: "StyleTargetType", Value: "typeof(ListBoxItem)"),
                ]),
        ];

        var rendered = AttributeFilter.RenderInlineList(attrs);

        await Assert.That(rendered).IsEqualTo("`[StyleTypedProperty(\"ItemContainerStyle\", StyleTargetType = typeof(ListBoxItem))]`");
    }

    /// <summary>Multiple surviving attributes are space-separated.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SeparatesMultipleSurvivorsBySpace()
    {
        ApiAttribute[] attrs =
        [
            new("Serializable", "T:System.SerializableAttribute", string.Empty, []),
            new("NullableContext", "T:System.Runtime.CompilerServices.NullableContextAttribute", string.Empty, []),
            new("Flags", "T:System.FlagsAttribute", string.Empty, []),
        ];

        var rendered = AttributeFilter.RenderInlineList(attrs);

        await Assert.That(rendered).IsEqualTo("`[Serializable]` `[Flags]`");
    }
}
