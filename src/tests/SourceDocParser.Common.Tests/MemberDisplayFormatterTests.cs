// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Common.Tests;

/// <summary>
/// Pins <see cref="MemberDisplayFormatter.FormatWithParens"/> +
/// <see cref="MemberDisplayFormatter.Concat"/>: the friendly
/// member-name builder both emitter packages share. Fast-path
/// (zero parameters) and slow-path (multi-parameter) must agree
/// with the format docfx and Microsoft Learn render.
/// </summary>
public class MemberDisplayFormatterTests
{
    /// <summary>Zero-parameter input takes the fast path: <c>label + "()"</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ZeroParamsRendersBareParens()
    {
        var result = MemberDisplayFormatter.FormatWithParens("Run", []);

        await Assert.That(result).IsEqualTo("Run()");
    }

    /// <summary>Single-parameter input goes through the slow path with no separator.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SingleParamRenders()
    {
        var result = MemberDisplayFormatter.FormatWithParens("Run", ["int"]);

        await Assert.That(result).IsEqualTo("Run(int)");
    }

    /// <summary>Multi-parameter input emits comma-space separators between every pair.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MultiParamSeparatesWithCommaSpace()
    {
        var result = MemberDisplayFormatter.FormatWithParens("Run", ["int", "string", "bool"]);

        await Assert.That(result).IsEqualTo("Run(int, string, bool)");
    }

    /// <summary>An empty label still produces well-formed output.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyLabelStillProducesParens()
    {
        var result = MemberDisplayFormatter.FormatWithParens(string.Empty, []);

        await Assert.That(result).IsEqualTo("()");
    }

    /// <summary>Concat of three parts joins them with the separator character.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConcatJoinsThreeParts()
    {
        var result = MemberDisplayFormatter.Concat("Foo", '.', "Bar");

        await Assert.That(result).IsEqualTo("Foo.Bar");
    }

    /// <summary>Concat short-circuits when the prefix is empty so global-namespace types don't get a leading dot.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConcatEmptyPrefixReturnsSuffix()
    {
        var result = MemberDisplayFormatter.Concat(string.Empty, '.', "Bar");

        await Assert.That(result).IsEqualTo("Bar");
    }
}
