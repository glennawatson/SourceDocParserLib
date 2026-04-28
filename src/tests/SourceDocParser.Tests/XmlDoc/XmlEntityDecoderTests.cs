// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests.XmlDoc;

/// <summary>
/// Direct coverage of <see cref="XmlEntityDecoder"/>: the standard
/// XML entity decoder + numeric character reference parser. Pins
/// every switch arm of <c>AppendDecoded</c> plus the documented
/// edge cases (malformed entity, unknown entity, mid-string ampersand,
/// out-of-range numeric reference).
/// </summary>
public class XmlEntityDecoderTests
{
    /// <summary>Standard XML entities decode to their canonical character.</summary>
    /// <param name="encoded">Entity-encoded source text.</param>
    /// <param name="expected">Expected decoded text.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("a&lt;b", "a<b")]
    [Arguments("a&gt;b", "a>b")]
    [Arguments("a&amp;b", "a&b")]
    [Arguments("a&quot;b", "a\"b")]
    [Arguments("a&apos;b", "a'b")]
    [Arguments("&#65;", "A")]
    [Arguments("&#x41;", "A")]
    [Arguments("&#x2A;", "*")]
    [Arguments("plain", "plain")]
    public async Task AppendDecodedExpandsKnownEntities(string encoded, string expected)
    {
        var sb = new StringBuilder();
        XmlEntityDecoder.AppendDecoded(sb, encoded.AsSpan());
        await Assert.That(sb.ToString()).IsEqualTo(expected);
    }

    /// <summary>Unknown entities are silently dropped (matching XmlReader's behaviour for undefined entities).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendDecodedDropsUnknownEntity()
    {
        var sb = new StringBuilder();
        XmlEntityDecoder.AppendDecoded(sb, "a&xyz;b".AsSpan());
        await Assert.That(sb.ToString()).IsEqualTo("ab");
    }

    /// <summary>An ampersand without a closing semicolon is appended verbatim -- the malformed-entity contract.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendDecodedAppendsMalformedEntityVerbatim()
    {
        var sb = new StringBuilder();
        XmlEntityDecoder.AppendDecoded(sb, "before&unclosed".AsSpan());
        await Assert.That(sb.ToString()).IsEqualTo("before&unclosed");
    }

    /// <summary>Numeric references that overflow the BMP are silently dropped (out-of-range).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendDecodedDropsOutOfRangeNumericRef()
    {
        var sb = new StringBuilder();
        XmlEntityDecoder.AppendDecoded(sb, "a&#x10000;b".AsSpan());
        await Assert.That(sb.ToString()).IsEqualTo("ab");
    }

    /// <summary>An empty input produces an empty append -- the caller's StringBuilder isn't mutated.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendDecodedNoOpForEmptyInput()
    {
        var sb = new StringBuilder("seed");
        XmlEntityDecoder.AppendDecoded(sb, default);
        await Assert.That(sb.ToString()).IsEqualTo("seed");
    }

    /// <summary>Multiple adjacent entities all decode in one pass.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendDecodedHandlesMultipleAdjacentEntities()
    {
        var sb = new StringBuilder();
        XmlEntityDecoder.AppendDecoded(sb, "&lt;&amp;&gt;".AsSpan());
        await Assert.That(sb.ToString()).IsEqualTo("<&>");
    }

    /// <summary>AppendDecoded validates its arguments.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendDecodedValidatesArguments() =>
        await Assert.That(static () => XmlEntityDecoder.AppendDecoded(null!, default)).Throws<ArgumentNullException>();

    /// <summary>TryParseNumericRef parses decimal digits.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryParseNumericRefHandlesDecimal()
    {
        await Assert.That(XmlEntityDecoder.TryParseNumericRef("65".AsSpan(), out var rune)).IsTrue();
        await Assert.That(rune).IsEqualTo('A');
    }

    /// <summary>TryParseNumericRef parses hex digits with both <c>x</c> and <c>X</c> prefixes.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryParseNumericRefHandlesHexBothCasings()
    {
        await Assert.That(XmlEntityDecoder.TryParseNumericRef("x41".AsSpan(), out var lower)).IsTrue();
        await Assert.That(lower).IsEqualTo('A');

        await Assert.That(XmlEntityDecoder.TryParseNumericRef("X41".AsSpan(), out var upper)).IsTrue();
        await Assert.That(upper).IsEqualTo('A');
    }

    /// <summary>TryParseNumericRef rejects an empty body.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryParseNumericRefRejectsEmptyBody()
    {
        await Assert.That(XmlEntityDecoder.TryParseNumericRef(default, out var rune)).IsFalse();
        await Assert.That(rune).IsEqualTo('\0');
    }

    /// <summary>TryParseNumericRef rejects non-numeric content.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryParseNumericRefRejectsNonNumeric()
    {
        await Assert.That(XmlEntityDecoder.TryParseNumericRef("abc".AsSpan(), out _)).IsFalse();
        await Assert.That(XmlEntityDecoder.TryParseNumericRef("xZZ".AsSpan(), out _)).IsFalse();
    }

    /// <summary>TryParseNumericRef rejects code points above the BMP (we work on chars, not Runes).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryParseNumericRefRejectsAboveBmp() => await Assert.That(XmlEntityDecoder.TryParseNumericRef("x10000".AsSpan(), out _)).IsFalse();

    /// <summary>TryParseNumericRef rejects negative values (defensive -- int.TryParse accepts a leading minus).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryParseNumericRefRejectsNegative() => await Assert.That(XmlEntityDecoder.TryParseNumericRef("-5".AsSpan(), out _)).IsFalse();
}
