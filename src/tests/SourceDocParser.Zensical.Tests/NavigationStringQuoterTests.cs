// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Zensical.Navigation;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins the escape branches of <see cref="NavigationStringQuoter"/>
/// -- the bare-scalar fast path is exercised by every existing
/// nav fixture, but the embedded-quote and embedded-backslash slow
/// paths only fire on values that don't appear in our sample types.
/// </summary>
public class NavigationStringQuoterTests
{
    /// <summary>QuoteString rejects null input.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QuoteStringRejectsNull() =>
        await Assert.That(() => NavigationStringQuoter.QuoteString(null!, escapeBackslashes: false)).Throws<ArgumentNullException>();

    /// <summary>A value with no escape-required chars wraps in quotes only.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QuoteStringWrapsCleanInputInQuotes()
    {
        var quoted = NavigationStringQuoter.QuoteString("Foo", escapeBackslashes: false);

        await Assert.That(quoted).IsEqualTo("\"Foo\"");
    }

    /// <summary>An embedded double quote is escaped with a backslash.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QuoteStringEscapesEmbeddedQuote()
    {
        var quoted = NavigationStringQuoter.QuoteString("a\"b", escapeBackslashes: false);

        await Assert.That(quoted).IsEqualTo("\"a\\\"b\"");
    }

    /// <summary>With escapeBackslashes=true an embedded backslash is escaped too.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QuoteStringEscapesBackslashesWhenAsked()
    {
        var quoted = NavigationStringQuoter.QuoteString(@"a\b", escapeBackslashes: true);

        await Assert.That(quoted).IsEqualTo(@"""a\\b""");
    }

    /// <summary>Multiple escape-required characters all get encoded.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QuoteStringEscapesMultipleOccurrences()
    {
        var quoted = NavigationStringQuoter.QuoteString("\"a\"b\"", escapeBackslashes: false);

        await Assert.That(quoted).IsEqualTo("\"\\\"a\\\"b\\\"\"");
    }

    /// <summary>A YAML scalar without reserved chars passes through unquoted.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task YamlScalarLeavesPlainTextBare() => await Assert.That(NavigationStringQuoter.YamlScalar("Plain")).IsEqualTo("Plain");

    /// <summary>A YAML scalar containing a reserved char (e.g. <c>:</c>) gets quoted.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task YamlScalarQuotesReservedChars() => await Assert.That(NavigationStringQuoter.YamlScalar("a:b")).IsEqualTo("\"a:b\"");

    /// <summary>TomlString always quotes and escapes both backslash and quote.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TomlStringEscapesBothBackslashAndQuote() => await Assert.That(NavigationStringQuoter.TomlString("a\\b\"c")).IsEqualTo("\"a\\\\b\\\"c\"");

    /// <summary>CountEscapes excludes backslashes when escapeBackslashes is false.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CountEscapesIgnoresBackslashesWhenDisabled()
    {
        await Assert.That(NavigationStringQuoter.CountEscapes("a\\b\"c", escapeBackslashes: false)).IsEqualTo(1);
        await Assert.That(NavigationStringQuoter.CountEscapes("a\\b\"c", escapeBackslashes: true)).IsEqualTo(2);
    }
}
