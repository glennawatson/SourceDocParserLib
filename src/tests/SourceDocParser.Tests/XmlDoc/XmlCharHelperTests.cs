// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests.XmlDoc;

/// <summary>
/// Pins <see cref="XmlCharHelper.IsWhitespace"/> — the four
/// XML-significant whitespace characters (space, tab, CR, LF) and
/// nothing else.
/// </summary>
public class XmlCharHelperTests
{
    /// <summary>The four allowed whitespace characters return true.</summary>
    /// <param name="ch">Character under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(' ')]
    [Arguments('\t')]
    [Arguments('\r')]
    [Arguments('\n')]
    public async Task IsWhitespaceTrueForAllowedChars(char ch) =>
        await Assert.That(XmlCharHelper.IsWhitespace(ch)).IsTrue();

    /// <summary>Non-whitespace characters return false (including vertical tab and form-feed).</summary>
    /// <param name="ch">Character under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments('a')]
    [Arguments('Z')]
    [Arguments('0')]
    [Arguments('<')]
    [Arguments('>')]
    [Arguments('=')]
    [Arguments('"')]
    [Arguments('\v')]
    [Arguments('\f')]
    [Arguments('\0')]
    public async Task IsWhitespaceFalseForOtherChars(char ch) =>
        await Assert.That(XmlCharHelper.IsWhitespace(ch)).IsFalse();
}
