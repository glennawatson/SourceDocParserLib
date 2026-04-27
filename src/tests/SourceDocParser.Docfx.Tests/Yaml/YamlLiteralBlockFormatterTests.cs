// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.Docfx.Yaml;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins <see cref="YamlLiteralBlockFormatter"/>: writes a YAML
/// literal-block scalar (<c>key |-</c> followed by an indented body)
/// at the right indent level. Indent is derived from the prefix's
/// leading-space count plus two — the standard YAML literal-block
/// continuation indent. Tested in isolation so a regression in the
/// indent calculation surfaces on its own line.
/// </summary>
public class YamlLiteralBlockFormatterTests
{
    /// <summary>The body indent equals the key indent plus two spaces.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BodyIndentIsKeyIndentPlusTwo()
    {
        var sb = new StringBuilder();

        YamlLiteralBlockFormatter.Format(sb, "  summary: ", "line one\nline two");

        // Key sits at 2 spaces; body lines at 4 spaces (key + 2).
        await Assert.That(sb.ToString()).IsEqualTo("  summary: |-\n    line one\n    line two\n");
    }

    /// <summary>A zero-indent prefix still adds the two-space body indent.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ZeroIndentPrefixStillIndentsBody()
    {
        var sb = new StringBuilder();

        YamlLiteralBlockFormatter.Format(sb, "summary: ", "alpha\nbeta");

        await Assert.That(sb.ToString()).IsEqualTo("summary: |-\n  alpha\n  beta\n");
    }

    /// <summary>Single-line body still emits one indented body line.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SingleLineBodyEmitsOneBodyLine()
    {
        var sb = new StringBuilder();

        YamlLiteralBlockFormatter.Format(sb, "    remarks: ", "alone");

        // Key sits at 4; body at 6.
        await Assert.That(sb.ToString()).IsEqualTo("    remarks: |-\n      alone\n");
    }

    /// <summary>ComputeIndentLength returns the leading-space count.</summary>
    /// <param name="prefix">Prefix to inspect.</param>
    /// <param name="expected">Expected leading-space count.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("summary: ", 0)]
    [Arguments("  summary: ", 2)]
    [Arguments("      remarks: ", 6)]
    public async Task ComputeIndentLengthCountsLeadingSpaces(string prefix, int expected) =>
        await Assert.That(YamlLiteralBlockFormatter.ComputeIndentLength(prefix)).IsEqualTo(expected);

    /// <summary>An all-spaces prefix returns the full prefix length (degenerate but defined).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AllSpacesPrefixReturnsFullLength()
    {
        await Assert.That(YamlLiteralBlockFormatter.ComputeIndentLength("    ")).IsEqualTo(4);
    }
}
