// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins the well-known BCL base synthesis: every class type
/// inherits <see cref="object"/>, structs <see cref="ValueType"/>,
/// enums <see cref="Enum"/>, delegates <see cref="MulticastDelegate"/>.
/// Walker filters those bases from the model, so the docfx emitter
/// has to put them back to match docfx's own output.
/// </summary>
public class DocfxWellKnownBasesTests
{
    /// <summary>Class types render <c>inheritance: System.Object</c> even though the walker null'd the BaseType.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassEmitsObjectInheritance()
    {
        var yaml = DocfxYamlEmitter.Render(TestData.ObjectType("Foo"));

        await Assert.That(yaml).Contains("inheritance:\n  - System.Object");
    }

    /// <summary>Enum types render <c>inheritance: System.Enum</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumEmitsEnumInheritance()
    {
        var yaml = DocfxYamlEmitter.Render(TestData.EnumType("Day"));

        await Assert.That(yaml).Contains("inheritance:\n  - System.Enum");
    }

    /// <summary>Delegate types render <c>inheritance: System.MulticastDelegate</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DelegateEmitsMulticastDelegateInheritance()
    {
        var yaml = DocfxYamlEmitter.Render(TestData.DelegateType("Handler"));

        await Assert.That(yaml).Contains("inheritance:\n  - System.MulticastDelegate");
    }

    /// <summary>Synthesised base references show up in the page-level <c>references:</c> block.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SynthesisedBaseAppearsInReferences()
    {
        var yaml = DocfxYamlEmitter.Render(TestData.ObjectType("Foo"));

        await Assert.That(yaml).Contains("- uid: System.Object");
    }

    /// <summary>Interface types skip the synthesis (no implicit base).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InterfaceSkipsSynthesisedBase()
    {
        var iface = TestData.ObjectType("IFoo", ApiObjectKind.Interface);

        var yaml = DocfxYamlEmitter.Render(iface);

        await Assert.That(yaml).DoesNotContain("inheritance:");
    }
}
