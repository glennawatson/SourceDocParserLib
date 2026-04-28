// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="DefaultCrefResolver"/> -- the backwards-compatible
/// resolver used when callers don't wire up a custom <see cref="ICrefResolver"/>.
/// Covers the three rendering branches (null/empty UID, generic-parameter
/// <c>!:T</c> sentinel, and the standard autoref form) plus the singleton.
/// </summary>
public class DefaultCrefResolverTests
{
    /// <summary>The shared singleton is non-null and reused across calls.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstanceIsSharedSingleton()
    {
        await Assert.That(DefaultCrefResolver.Instance).IsNotNull();
        await Assert.That(DefaultCrefResolver.Instance).IsSameReferenceAs(DefaultCrefResolver.Instance);
    }

    /// <summary>A standard type UID renders as the autoref <c>[shortName][uid]</c> form.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderProducesAutorefForTypeUid() =>
        await Assert.That(DefaultCrefResolver.Instance.Render("T:System.String", "String".AsSpan()))
            .IsEqualTo("[String][T:System.String]");

    /// <summary>A method UID renders as the autoref <c>[shortName][uid]</c> form.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderProducesAutorefForMethodUid() =>
        await Assert.That(DefaultCrefResolver.Instance.Render("M:Foo.Bar(System.Int32)", "Bar".AsSpan()))
            .IsEqualTo("[Bar][M:Foo.Bar(System.Int32)]");

    /// <summary>A null UID falls back to inline-code rendering of the short name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderFallsBackToInlineCodeForNullUid() =>
        await Assert.That(DefaultCrefResolver.Instance.Render(null!, "Foo".AsSpan()))
            .IsEqualTo("`Foo`");

    /// <summary>An empty UID falls back to inline-code rendering of the short name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderFallsBackToInlineCodeForEmptyUid() =>
        await Assert.That(DefaultCrefResolver.Instance.Render(string.Empty, "Foo".AsSpan()))
            .IsEqualTo("`Foo`");

    /// <summary>Roslyn's <c>!:T</c> generic-parameter sentinel renders as inline code.</summary>
    /// <param name="uid">The generic-parameter sentinel UID variant under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("!:T")]
    [Arguments("!:TResult")]
    [Arguments("!:TKey")]
    public async Task RenderEmitsInlineCodeForGenericParameterSentinel(string uid) =>
        await Assert.That(DefaultCrefResolver.Instance.Render(uid, "T".AsSpan()))
            .IsEqualTo("`T`");

    /// <summary>The short name (not the UID) is the inline-code body for unresolvable refs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderUsesShortNameAsInlineCodeBody() =>
        await Assert.That(DefaultCrefResolver.Instance.Render("!:TResult", "TResult".AsSpan()))
            .IsEqualTo("`TResult`");
}
