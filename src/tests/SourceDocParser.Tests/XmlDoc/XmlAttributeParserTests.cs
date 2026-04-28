// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests.XmlDoc;

/// <summary>
/// Direct coverage of <see cref="XmlAttributeParser"/>: parses the
/// attribute area carved off a start tag by
/// <see cref="XmlMarkupParser.ReadStartElement"/>. Only double-quoted
/// values are recognised -- mirrors the contract the doc scanner
/// relies on.
/// </summary>
public class XmlAttributeParserTests
{
    /// <summary>An empty attribute area yields an empty span lookup.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeReturnsEmptyForEmptyArea()
    {
        var value = XmlAttributeParser.GetAttribute(default, "name".AsSpan());
        await Assert.That(value.IsEmpty).IsTrue();
    }

    /// <summary>The first matching attribute name returns its value.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeReturnsValueForKnownAttribute()
    {
        var area = " cref=\"T:Foo\" name=\"x\"".AsSpan();
        var cref = XmlAttributeParser.GetAttribute(area, "cref".AsSpan()).ToString();
        var name = XmlAttributeParser.GetAttribute(area, "name".AsSpan()).ToString();

        await Assert.That(cref).IsEqualTo("T:Foo");
        await Assert.That(name).IsEqualTo("x");
    }

    /// <summary>An unknown attribute returns an empty span.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeReturnsEmptyForUnknownAttribute()
    {
        var value = XmlAttributeParser.GetAttribute(" cref=\"T:Foo\"".AsSpan(), "missing".AsSpan());
        await Assert.That(value.IsEmpty).IsTrue();
    }

    /// <summary>An empty quoted value is returned as an empty (but matched) span.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeReturnsEmptyValueForEmptyQuotedString()
    {
        const string source = " name=\"\" cref=\"T:Foo\"";
        var nameLen = XmlAttributeParser.GetAttribute(source.AsSpan(), "name".AsSpan()).Length;
        var cref = XmlAttributeParser.GetAttribute(source.AsSpan(), "cref".AsSpan()).ToString();

        await Assert.That(nameLen).IsEqualTo(0);
        await Assert.That(cref).IsEqualTo("T:Foo");
    }

    /// <summary>Single-quoted values are not recognised -- contract is double-quote only.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeIgnoresSingleQuotedValue()
    {
        var value = XmlAttributeParser.GetAttribute(" name='x'".AsSpan(), "name".AsSpan());
        await Assert.That(value.IsEmpty).IsTrue();
    }

    /// <summary>An unterminated quoted value aborts the scan.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeReturnsEmptyWhenQuotedValueIsUnterminated()
    {
        var value = XmlAttributeParser.GetAttribute(" name=\"unclosed".AsSpan(), "name".AsSpan());
        await Assert.That(value.IsEmpty).IsTrue();
    }

    /// <summary>Whitespace is skipped between attributes.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeHandlesExtraWhitespace()
    {
        var area = "   cref =   \"T:Foo\"   name=\"x\"".AsSpan();
        var cref = XmlAttributeParser.GetAttribute(area, "cref".AsSpan()).ToString();
        await Assert.That(cref).IsEqualTo("T:Foo");
    }

    /// <summary>Multiple attributes after a match are still findable when scanning order matters.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeFindsLaterAttributesAfterEarlier()
    {
        const string source = " cref=\"T:Foo\" name=\"x\"";
        var name = XmlAttributeParser.GetAttribute(source.AsSpan(), "name".AsSpan()).ToString();
        await Assert.That(name).IsEqualTo("x");
    }
}
