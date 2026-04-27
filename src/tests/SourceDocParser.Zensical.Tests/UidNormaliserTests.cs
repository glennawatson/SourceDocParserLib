// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins <see cref="UidNormaliser"/> on the constructed-generic
/// rewrite shape — the bug we identified in the docfx slice
/// where the walker emits <c>T:System.Action{`0}</c> instead of
/// the canonical open-generic <c>T:System.Action`1</c>.
/// </summary>
public class UidNormaliserTests
{
    /// <summary>Single type-arg constructed form folds back to arity 1.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NormalisesSingleTypeArgConstructedForm() => await Assert.That(UidNormaliser.Normalise("T:System.Action{`0}")).IsEqualTo("T:System.Action`1");

    /// <summary>Multi-type-arg constructed form folds back to its arity.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NormalisesMultiTypeArgConstructedForm() => await Assert.That(UidNormaliser.Normalise("T:System.Func{`0,`1,`2}")).IsEqualTo("T:System.Func`3");

    /// <summary>Concrete type-arg lists collapse to arity too.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NormalisesConcreteTypeArgList() =>
        await Assert.That(UidNormaliser.Normalise("T:ReactiveUI.IBindingTypeConverter{System.Boolean,System.String}"))
            .IsEqualTo("T:ReactiveUI.IBindingTypeConverter`2");

    /// <summary>Nested type-arg braces don't inflate the count.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NestedBracesCountAsOneTopLevelArg() =>
        await Assert.That(UidNormaliser.Normalise("T:System.Func{System.IObservable{`0}}"))
            .IsEqualTo("T:System.Func`1");

    /// <summary>Already-canonical UIDs pass through unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AlreadyCanonicalIsUnchanged()
    {
        await Assert.That(UidNormaliser.Normalise("T:System.String")).IsEqualTo("T:System.String");
        await Assert.That(UidNormaliser.Normalise("T:System.Action`1")).IsEqualTo("T:System.Action`1");
    }

    /// <summary>Member UIDs aren't touched (signatures legitimately carry braces).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MemberUidsArePassedThrough()
    {
        const string memberUid = "M:Foo.Bar(System.Func{System.Int32,System.String})";
        await Assert.That(UidNormaliser.Normalise(memberUid)).IsEqualTo(memberUid);
    }

    /// <summary>Empty / no-prefix inputs return unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyAndUnprefixedReturnUnchanged()
    {
        await Assert.That(UidNormaliser.Normalise(string.Empty)).IsEqualTo(string.Empty);
        await Assert.That(UidNormaliser.Normalise("Foo.Bar")).IsEqualTo("Foo.Bar");
    }

    /// <summary>A bare-name (no T: prefix) constructed form still folds.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BareNameConstructedFormFolds() => await Assert.That(UidNormaliser.Normalise("System.Action{`0,`1}")).IsEqualTo("System.Action`2");

    /// <summary>A type with no <c>{</c> at all passes through unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypeWithoutBraceIsUnchanged() =>
        await Assert.That(UidNormaliser.Normalise("T:System.Boring")).IsEqualTo("T:System.Boring");

    /// <summary>A malformed UID with an open <c>{</c> but no matching close passes through unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MalformedOpenBraceIsUnchanged() =>
        await Assert.That(UidNormaliser.Normalise("T:System.Action{`0")).IsEqualTo("T:System.Action{`0");

    /// <summary>A close-brace with trailing content (e.g. an array suffix) is left alone.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BraceWithTrailingContentIsUnchanged() =>
        await Assert.That(UidNormaliser.Normalise("T:System.Action{`0}[]")).IsEqualTo("T:System.Action{`0}[]");

    /// <summary>An empty brace pair counts as zero arity and falls back to the original name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyBracesAreUnchanged() =>
        await Assert.That(UidNormaliser.Normalise("T:System.Action{}")).IsEqualTo("T:System.Action{}");
}
