// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins <c>DocfxReferenceEnricher.SynthesiseFullName</c>: docfx's
/// <c>fullName</c> field stays fully namespaced (unlike <c>name</c>),
/// constructed generics swap <c>{…}</c> braces for <c>&lt;…&gt;</c>,
/// the arity backtick is dropped from the base, BCL primitives still
/// alias to their C# keyword form, and nested generics recurse.
/// </summary>
public class DocfxReferenceFullNameTests
{
    /// <summary>Plain non-generic UIDs round-trip with namespace intact.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NonGenericPreservesNamespace()
    {
        var result = DocfxReferenceEnricher.SynthesiseFullName("My.Foo");

        await Assert.That(result).IsEqualTo("My.Foo");
    }

    /// <summary>BCL primitive class types alias to their keyword form.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BclPrimitiveAliasesToKeyword()
    {
        var result = DocfxReferenceEnricher.SynthesiseFullName("System.Object");

        await Assert.That(result).IsEqualTo("object");
    }

    /// <summary>Constructed generics produce <c>Namespace.Base&lt;Arg&gt;</c> with arity stripped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SingleArgGenericExpandsToAngles()
    {
        var result = DocfxReferenceEnricher.SynthesiseFullName("System.IObservable`1{System.Int32}");

        await Assert.That(result).IsEqualTo("System.IObservable<int>");
    }

    /// <summary>Multi-arg generics emit comma-space separators.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MultiArgGenericSeparatesWithCommaSpace()
    {
        var result = DocfxReferenceEnricher.SynthesiseFullName("System.Func`2{System.Int32,System.String}");

        await Assert.That(result).IsEqualTo("System.Func<int, string>");
    }

    /// <summary>Nested generics recurse so each layer renders in docfx form.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NestedGenericRecurses()
    {
        var result = DocfxReferenceEnricher.SynthesiseFullName(
            "System.Func`1{System.IObservable`1{System.Int32}}");

        await Assert.That(result).IsEqualTo("System.Func<System.IObservable<int>>");
    }
}
