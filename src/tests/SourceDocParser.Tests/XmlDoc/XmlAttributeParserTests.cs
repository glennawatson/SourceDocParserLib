// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests.XmlDoc;

/// <summary>
/// Direct coverage of <see cref="XmlAttributeParser"/>: parses the
/// attribute area carved off a start tag by
/// <see cref="XmlMarkupParser.ReadStartElement"/>. Only double-quoted
/// values are recognised — mirrors the contract the doc scanner
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

    /// <summary>Single-quoted values are not recognised — contract is double-quote only.</summary>
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

    /// <summary>Reading sequential attributes advances the index past each value.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadNextAttributeAdvancesPastEachAttribute()
    {
        const string source = " cref=\"T:Foo\" name=\"x\"";
        var index = 0;

        var first = XmlAttributeParser.ReadNextAttribute(source.AsSpan(), ref index);
        var firstValid = first.IsValid;
        var firstName = source.Substring(first.NameStart, first.NameLength);
        var firstValue = source.Substring(first.ValueStart, first.ValueLength);

        var second = XmlAttributeParser.ReadNextAttribute(source.AsSpan(), ref index);
        var secondValid = second.IsValid;
        var secondName = source.Substring(second.NameStart, second.NameLength);
        var secondValue = source.Substring(second.ValueStart, second.ValueLength);

        await Assert.That(firstValid).IsTrue();
        await Assert.That(firstName).IsEqualTo("cref");
        await Assert.That(firstValue).IsEqualTo("T:Foo");
        await Assert.That(secondValid).IsTrue();
        await Assert.That(secondName).IsEqualTo("name");
        await Assert.That(secondValue).IsEqualTo("x");
    }

    /// <summary>Reading past the last attribute returns the default (invalid) range.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadNextAttributeReturnsDefaultPastEnd()
    {
        var area = " cref=\"T:Foo\"".AsSpan();
        var index = 0;
        XmlAttributeParser.ReadNextAttribute(area, ref index);

        var third = XmlAttributeParser.ReadNextAttribute(area, ref index);
        await Assert.That(third.IsValid).IsFalse();
    }

    /// <summary>Skipping whitespace lands on the next non-space character.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SkipWhitespaceLandsOnNextNonSpace()
    {
        var area = "   x".AsSpan();
        var index = XmlAttributeParser.SkipWhitespace(area, 0);
        await Assert.That(index).IsEqualTo(3);
    }

    /// <summary>Skipping whitespace from end-of-span returns the span length.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SkipWhitespaceReturnsLengthForAllWhitespace()
    {
        var area = "   ".AsSpan();
        var index = XmlAttributeParser.SkipWhitespace(area, 0);
        await Assert.That(index).IsEqualTo(area.Length);
    }

    /// <summary>The name reader stops at <c>=</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AdvancePastAttributeNameStopsAtEquals()
    {
        var area = "cref=\"T:Foo\"".AsSpan();
        var end = XmlAttributeParser.AdvancePastAttributeName(area, 0);
        await Assert.That(end).IsEqualTo("cref".Length);
    }
}
