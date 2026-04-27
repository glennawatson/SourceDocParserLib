// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins <see cref="UidNormalization"/>: the UID-string toolkit shared
/// across the docfx YAML emitter — strip prefix, strip arity backtick,
/// derive parent namespace, project to open-generic form, synthesise
/// the docfx <c>fullName</c>, and walk brace regions for nested type
/// arguments. Tested in isolation so a regression in any one rule
/// surfaces on its own line.
/// </summary>
public class UidNormalizationTests
{
    /// <summary>StripPrefix removes the two-character Roslyn prefix.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StripPrefixRemovesRoslynPrefix()
    {
        await Assert.That(UidNormalization.StripPrefix("T:Foo.Bar")).IsEqualTo("Foo.Bar");
        await Assert.That(UidNormalization.StripPrefix("M:Foo.Run(System.Int32)")).IsEqualTo("Foo.Run(System.Int32)");
        await Assert.That(UidNormalization.StripPrefix("Foo.Bar")).IsEqualTo("Foo.Bar");
    }

    /// <summary>StripArityBacktick drops the trailing <c>`N</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StripArityBacktickRemovesArityWhenPresent()
    {
        await Assert.That(UidNormalization.StripArityBacktick("System.Action`1")).IsEqualTo("System.Action");
        await Assert.That(UidNormalization.StripArityBacktick("System.Action")).IsEqualTo("System.Action");
        await Assert.That(UidNormalization.StripArityBacktick("Foo`12")).IsEqualTo("Foo");
    }

    /// <summary>ParentOf returns the namespace part of a dotted name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParentOfReturnsNamespace()
    {
        await Assert.That(UidNormalization.ParentOf("My.Sub.Foo")).IsEqualTo("My.Sub");
        await Assert.That(UidNormalization.ParentOf("Foo")).IsEqualTo(string.Empty);
    }

    /// <summary>ParentOf walks above the brace boundary so type-arg dots aren't consumed.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParentOfRespectsBraceBoundary() =>

        // Foo.Bar`1{Baz.Qux} — the dot inside the brace region must
        // not be picked as the namespace boundary.
        await Assert.That(UidNormalization.ParentOf("My.Sub.Foo`1{Baz.Qux}")).IsEqualTo("My.Sub");

    /// <summary>ToOpenGenericUid synthesises <c>`N</c> when the walker omits it.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ToOpenGenericUidAddsArityBacktick()
    {
        await Assert.That(UidNormalization.ToOpenGenericUid("T:System.IObservable{System.Int32}"))
            .IsEqualTo("T:System.IObservable`1");
        await Assert.That(UidNormalization.ToOpenGenericUid("T:System.Func{System.Int32,System.String}"))
            .IsEqualTo("T:System.Func`2");
    }

    /// <summary>ToOpenGenericUid passes through non-generic UIDs unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ToOpenGenericUidPassesThroughNonGeneric() => await Assert.That(UidNormalization.ToOpenGenericUid("T:My.Foo")).IsEqualTo("T:My.Foo");

    /// <summary>ToOpenGenericUid keeps the existing arity backtick when the walker already provided one.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ToOpenGenericUidPreservesExistingBacktick() =>

        // When the head already carries `1, the helper returns the bare
        // head without re-counting (avoids double-counting when the
        // walker pre-populates the arity).
        await Assert.That(UidNormalization.ToOpenGenericUid("T:My.Foo`1{Bar}")).IsEqualTo("T:My.Foo`1");

    /// <summary>SynthesiseFullName: plain non-generic UIDs round-trip with namespace intact.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SynthesiseFullNamePreservesNamespace() =>
        await Assert.That(UidNormalization.SynthesiseFullName("My.Foo")).IsEqualTo("My.Foo");

    /// <summary>SynthesiseFullName: BCL primitive class types alias to their C# keyword form.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SynthesiseFullNameAliasesBclPrimitiveToKeyword() =>
        await Assert.That(UidNormalization.SynthesiseFullName("System.Object")).IsEqualTo("object");

    /// <summary>SynthesiseFullName: single-arg generic produces <c>Namespace.Base&lt;Arg&gt;</c> with arity stripped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SynthesiseFullNameExpandsSingleArgGenericToAngles() =>
        await Assert.That(UidNormalization.SynthesiseFullName("System.IObservable`1{System.Int32}"))
            .IsEqualTo("System.IObservable<int>");

    /// <summary>SynthesiseFullName: multi-arg generics emit <c>, </c> separators between type args.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SynthesiseFullNameSeparatesMultiArgWithCommaSpace() =>
        await Assert.That(UidNormalization.SynthesiseFullName("System.Func`2{System.Int32,System.String}"))
            .IsEqualTo("System.Func<int, string>");

    /// <summary>SynthesiseFullName: nested generics recurse so each layer renders in docfx form.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SynthesiseFullNameRecursesIntoNestedGenerics() =>
        await Assert.That(UidNormalization.SynthesiseFullName(
            "System.Func`1{System.IObservable`1{System.Int32}}"))
            .IsEqualTo("System.Func<System.IObservable<int>>");

    /// <summary>SplitTopLevelArgs ignores commas inside nested bracket pairs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SplitTopLevelArgsIgnoresNestedCommas()
    {
        var pieces = UidNormalization.SplitTopLevelArgs("Int32,Func{Int32,String},Bool", '{', '}');

        string[] expected = ["Int32", "Func{Int32,String}", "Bool"];
        await Assert.That(pieces).IsEquivalentTo(expected);
    }

    /// <summary>CountTopLevelArgs counts top-level commas plus one.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CountTopLevelArgsCountsCommasPlusOne()
    {
        await Assert.That(UidNormalization.CountTopLevelArgs("A", '{', '}')).IsEqualTo(1);
        await Assert.That(UidNormalization.CountTopLevelArgs("A,B", '{', '}')).IsEqualTo(2);
        await Assert.That(UidNormalization.CountTopLevelArgs("A,Foo{X,Y},B", '{', '}')).IsEqualTo(3);
    }

    /// <summary>CountTopLevelArgsInUidBraces stops at the matching close brace.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CountTopLevelArgsInUidBracesStopsAtMatchingBrace()
    {
        const string uid = "T:Foo{A,B,C}.Bar";
        var openIdx = uid.IndexOf('{', StringComparison.Ordinal);

        await Assert.That(UidNormalization.CountTopLevelArgsInUidBraces(uid, openIdx)).IsEqualTo(3);
    }
}
