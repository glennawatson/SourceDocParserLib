// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Walk;

namespace SourceDocParser.Tests.Walk;

/// <summary>
/// Direct coverage of the pure helpers behind
/// <see cref="AttributeExtractor"/>: the <c>Attribute</c>-suffix
/// stripper, the C# string literal quoter (with escape handling),
/// and the boxed-primitive formatter. These run on every walked
/// attribute usage; testing them in isolation pins each branch in
/// the switch arms without needing synthetic AttributeData.
/// </summary>
public class AttributeExtractorHelperTests
{
    /// <summary>Names ending in <c>Attribute</c> get the suffix stripped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StripAttributeSuffixDropsSuffixWhenPresent()
    {
        await Assert.That(AttributeExtractor.StripAttributeSuffix("ObsoleteAttribute")).IsEqualTo("Obsolete");
        await Assert.That(AttributeExtractor.StripAttributeSuffix("FlagsAttribute")).IsEqualTo("Flags");
    }

    /// <summary>Names that don't end in <c>Attribute</c> pass through unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StripAttributeSuffixPassesThroughWhenAbsent()
    {
        await Assert.That(AttributeExtractor.StripAttributeSuffix("Foo")).IsEqualTo("Foo");
        await Assert.That(AttributeExtractor.StripAttributeSuffix("MyType")).IsEqualTo("MyType");
    }

    /// <summary>The exact string <c>Attribute</c> stays as-is — stripping it would leave an empty name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StripAttributeSuffixKeepsBareAttribute() => await Assert.That(AttributeExtractor.StripAttributeSuffix("Attribute")).IsEqualTo("Attribute");

    /// <summary>QuoteStringLiteral wraps a no-escape string in double quotes via the fast path.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QuoteStringLiteralFastPathWrapsInQuotes()
    {
        await Assert.That(AttributeExtractor.QuoteStringLiteral("hello")).IsEqualTo("\"hello\"");
        await Assert.That(AttributeExtractor.QuoteStringLiteral(string.Empty)).IsEqualTo("\"\"");
    }

    /// <summary>QuoteStringLiteral escapes embedded backslashes and double quotes.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QuoteStringLiteralEscapesBackslashAndQuote()
    {
        await Assert.That(AttributeExtractor.QuoteStringLiteral(@"a\b")).IsEqualTo(@"""a\\b""");
        await Assert.That(AttributeExtractor.QuoteStringLiteral("a\"b")).IsEqualTo("\"a\\\"b\"");
        await Assert.That(AttributeExtractor.QuoteStringLiteral("\\\"")).IsEqualTo("\"\\\\\\\"\"");
    }

    /// <summary>QuoteStringLiteral preserves the leading non-escape prefix when escapes appear later in the string.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QuoteStringLiteralPreservesPrefixBeforeFirstEscape() => await Assert.That(AttributeExtractor.QuoteStringLiteral("prefix\"middle")).IsEqualTo("\"prefix\\\"middle\"");

    /// <summary>FormatPrimitive returns <c>null</c> for null values.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatPrimitiveHandlesNull() => await Assert.That(AttributeExtractor.FormatPrimitive(null)).IsEqualTo("null");

    /// <summary>FormatPrimitive renders strings via the QuoteStringLiteral path.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatPrimitiveQuotesStrings() => await Assert.That(AttributeExtractor.FormatPrimitive("hello")).IsEqualTo("\"hello\"");

    /// <summary>FormatPrimitive renders chars in single quotes.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatPrimitiveRendersChars() => await Assert.That(AttributeExtractor.FormatPrimitive('x')).IsEqualTo("'x'");

    /// <summary>FormatPrimitive renders bools as lowercase <c>true</c>/<c>false</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatPrimitiveLowercasesBools()
    {
        await Assert.That(AttributeExtractor.FormatPrimitive(true)).IsEqualTo("true");
        await Assert.That(AttributeExtractor.FormatPrimitive(false)).IsEqualTo("false");
    }

    /// <summary>FormatPrimitive routes IFormattable values through the invariant culture.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatPrimitiveRoutesNumbersThroughInvariantCulture()
    {
        await Assert.That(AttributeExtractor.FormatPrimitive(42)).IsEqualTo("42");
        await Assert.That(AttributeExtractor.FormatPrimitive(3.14)).IsEqualTo("3.14");
        await Assert.That(AttributeExtractor.FormatPrimitive(0L)).IsEqualTo("0");
    }

    /// <summary>FormatPrimitive falls back to <c>ToString()</c> for any boxed value that doesn't match the typed switch arms.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatPrimitiveFallsBackToToString()
    {
        var custom = new CustomFormattableless();

        await Assert.That(AttributeExtractor.FormatPrimitive(custom)).IsEqualTo("custom-toString");
    }

    /// <summary>CountEscapes counts every backslash and double quote in the span.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CountEscapesCountsBackslashAndQuote()
    {
        await Assert.That(AttributeExtractor.CountEscapes("plain".AsSpan())).IsEqualTo(0);
        await Assert.That(AttributeExtractor.CountEscapes("a\\b".AsSpan())).IsEqualTo(1);
        await Assert.That(AttributeExtractor.CountEscapes("a\"b".AsSpan())).IsEqualTo(1);
        await Assert.That(AttributeExtractor.CountEscapes("\\\"".AsSpan())).IsEqualTo(2);
        await Assert.That(AttributeExtractor.CountEscapes("\\\\\"\"".AsSpan())).IsEqualTo(4);
    }

    /// <summary>The <c>ObsoleteAttributeFullName</c> constant matches the actual <see cref="ObsoleteAttribute"/> fully-qualified name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ObsoleteAttributeFullNameMatchesBclType() => await Assert.That(AttributeExtractor.ObsoleteAttributeFullName).IsEqualTo(typeof(ObsoleteAttribute).FullName);

    /// <summary>Type with a custom <c>ToString</c> override — drives the FormatPrimitive fallback arm for boxed values that aren't string / char / bool / IFormattable.</summary>
    private sealed class CustomFormattableless
    {
        /// <inheritdoc />
        public override string ToString() => "custom-toString";
    }
}
