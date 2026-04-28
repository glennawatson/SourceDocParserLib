// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins <see cref="DocfxCrefResolver.Render"/>: the empty-UID and
/// <c>!:</c> generic-parameter fall back to inline code, every other
/// UID round-trips into docfx's <c>&lt;xref:UID?displayProperty=nameWithType&gt;</c>
/// form so the xrefmap step can resolve them at site-build time.
/// </summary>
public class DocfxCrefResolverTests
{
    /// <summary>The shared singleton is non-null and reusable.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstanceSingletonIsAvailable()
    {
        await Assert.That(DocfxCrefResolver.Instance).IsNotNull();
        await Assert.That(DocfxCrefResolver.Instance).IsSameReferenceAs(DocfxCrefResolver.Instance);
    }

    /// <summary>A null UID falls back to the inline-code rendering of the short name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NullUidFallsBackToInlineCode()
    {
        var rendered = DocfxCrefResolver.Instance.Render(uid: null!, "MyType".AsSpan());

        await Assert.That(rendered).IsEqualTo("`MyType`");
    }

    /// <summary>An empty UID falls back to the inline-code rendering of the short name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyUidFallsBackToInlineCode()
    {
        var rendered = DocfxCrefResolver.Instance.Render(string.Empty, "MyType".AsSpan());

        await Assert.That(rendered).IsEqualTo("`MyType`");
    }

    /// <summary>A <c>!:</c>-prefixed UID (unresolved generic parameter) falls back to inline code.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GenericParameterUidFallsBackToInlineCode()
    {
        var rendered = DocfxCrefResolver.Instance.Render("!:T", "T".AsSpan());

        await Assert.That(rendered).IsEqualTo("`T`");
    }

    /// <summary>A regular type UID renders as a docfx <c>xref</c> with the <c>nameWithType</c> display hint.</summary>
    /// <param name="uid">The UID under test.</param>
    /// <param name="shortName">The short name passed alongside the UID (ignored on the xref path).</param>
    /// <param name="expected">The expected rendered string.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("System.String", "String", "<xref:System.String?displayProperty=nameWithType>")]
    [Arguments("System.Collections.Generic.List`1", "List<T>", "<xref:System.Collections.Generic.List`1?displayProperty=nameWithType>")]
    [Arguments("M:System.String.Concat(System.String,System.String)", "Concat", "<xref:M:System.String.Concat(System.String,System.String)?displayProperty=nameWithType>")]
    public async Task RegularUidRendersAsXref(string uid, string shortName, string expected)
    {
        var rendered = DocfxCrefResolver.Instance.Render(uid, shortName.AsSpan());

        await Assert.That(rendered).IsEqualTo(expected);
    }

    /// <summary>A UID consisting solely of <c>!:</c> still hits the generic-parameter fallback.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BareGenericParameterPrefixFallsBackToInlineCode()
    {
        var rendered = DocfxCrefResolver.Instance.Render("!:", "TItem".AsSpan());

        await Assert.That(rendered).IsEqualTo("`TItem`");
    }

    /// <summary>A UID starting with a single <c>!</c> but not <c>!:</c> renders as an xref (only the two-char prefix triggers the fallback).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SingleBangPrefixDoesNotFallBack()
    {
        var rendered = DocfxCrefResolver.Instance.Render("!Foo", "Foo".AsSpan());

        await Assert.That(rendered).IsEqualTo("<xref:!Foo?displayProperty=nameWithType>");
    }
}
