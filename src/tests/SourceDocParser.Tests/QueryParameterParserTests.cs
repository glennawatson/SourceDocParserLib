// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="QueryParameterParser.Extract"/>: walks a raw query
/// string with span slicing to pluck the value of the first parameter
/// matching the requested name. Tested in isolation so a regression
/// in the boundary handling (no <c>=</c>, no <c>&amp;</c>, last entry
/// without trailing separator) surfaces on its own line.
/// </summary>
public class QueryParameterParserTests
{
    /// <summary>The first matching parameter wins; later occurrences are ignored.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractReturnsFirstMatchingValue()
    {
        await Assert.That(QueryParameterParser.Extract("foo=1&bar=2".AsSpan(), "foo")).IsEqualTo("1");
        await Assert.That(QueryParameterParser.Extract("foo=1&foo=2".AsSpan(), "foo")).IsEqualTo("1");
    }

    /// <summary>The last parameter (no trailing <c>&amp;</c>) is still readable.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractReadsLastParameter()
    {
        await Assert.That(QueryParameterParser.Extract("foo=1&bar=last".AsSpan(), "bar")).IsEqualTo("last");
    }

    /// <summary>Name match is case-insensitive.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractIsCaseInsensitiveOnName()
    {
        await Assert.That(QueryParameterParser.Extract("PATH=/foo".AsSpan(), "path")).IsEqualTo("/foo");
        await Assert.That(QueryParameterParser.Extract("path=/foo".AsSpan(), "PATH")).IsEqualTo("/foo");
    }

    /// <summary>Missing parameter returns <see langword="null"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractReturnsNullWhenAbsent()
    {
        await Assert.That(QueryParameterParser.Extract("foo=1&bar=2".AsSpan(), "baz")).IsNull();
        await Assert.That(QueryParameterParser.Extract([], "foo")).IsNull();
    }

    /// <summary>Empty value still returns the empty string (not null).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractReturnsEmptyStringForEmptyValue()
    {
        await Assert.That(QueryParameterParser.Extract("foo=&bar=2".AsSpan(), "foo")).IsEqualTo(string.Empty);
    }

    /// <summary>Pairs without an <c>=</c> sign are skipped (the parser requires <c>name=value</c> form).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractSkipsBareValueWithoutEquals()
    {
        await Assert.That(QueryParameterParser.Extract("flag&foo=1".AsSpan(), "flag")).IsNull();
        await Assert.That(QueryParameterParser.Extract("flag&foo=1".AsSpan(), "foo")).IsEqualTo("1");
    }
}
