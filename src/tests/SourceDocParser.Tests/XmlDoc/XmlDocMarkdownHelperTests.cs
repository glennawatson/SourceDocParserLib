// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests.XmlDoc;

/// <summary>
/// Direct coverage of the pure span helpers on
/// <see cref="XmlDocMarkdownHelper"/>: cref-name shortening,
/// table-cell escaping, blank-line / line-start whitespace control,
/// the whitespace collapser, and the public
/// <see cref="XmlDocMarkdownHelper.ConvertSpanToMarkdown(in System.ReadOnlySpan{char})"/> entry.
/// The tag-dispatch surfaces are exercised end-to-end via
/// <see cref="XmlDocToMarkdown"/>; this file pins the helpers in
/// isolation so a regression in one of them lights up directly.
/// </summary>
public class XmlDocMarkdownHelperTests
{
    /// <summary>An empty cell is rendered as a single space -- keeps GFM table columns aligned.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TableEscapeRendersEmptyAsSingleSpace() =>
        await Assert.That(XmlDocMarkdownHelper.TableEscape(string.Empty)).IsEqualTo(" ");

    /// <summary>Plain text passes through unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TableEscapeReturnsInputUnchangedWhenSafe() =>
        await Assert.That(XmlDocMarkdownHelper.TableEscape("hello world")).IsEqualTo("hello world");

    /// <summary>Pipes are backslash-escaped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TableEscapeEscapesPipes() =>
        await Assert.That(XmlDocMarkdownHelper.TableEscape("a|b|c")).IsEqualTo(@"a\|b\|c");

    /// <summary>Newlines and carriage returns become spaces.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TableEscapeReplacesNewlinesWithSpaces() =>
        await Assert.That(XmlDocMarkdownHelper.TableEscape("a\nb\r\nc")).IsEqualTo("a b  c");

    /// <summary>Mixed pipes and newlines are handled in a single pass.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TableEscapeHandlesMixedPipesAndNewlines() =>
        await Assert.That(XmlDocMarkdownHelper.TableEscape("a|b\nc|d")).IsEqualTo(@"a\|b c\|d");

    /// <summary>The cref kind prefix (T:, M:, etc.) is stripped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShortNameStripsKindPrefix() =>
        await Assert.That(XmlDocMarkdownHelper.ShortName("T:System.String".AsSpan()).ToString()).IsEqualTo("String");

    /// <summary>The parameter list is trimmed at the opening paren.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShortNameStripsParameterList() =>
        await Assert.That(XmlDocMarkdownHelper.ShortName("M:Foo.Bar.Baz(System.Int32)".AsSpan()).ToString()).IsEqualTo("Baz");

    /// <summary>Generic-arity backticks are removed.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShortNameStripsGenericBacktick() =>
        await Assert.That(XmlDocMarkdownHelper.ShortName("T:System.Collections.Generic.List`1".AsSpan()).ToString()).IsEqualTo("List");

    /// <summary>Names without a kind prefix are returned with namespace stripped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShortNameHandlesPlainName() =>
        await Assert.That(XmlDocMarkdownHelper.ShortName("System.String".AsSpan()).ToString()).IsEqualTo("String");

    /// <summary>Names that are already short are returned untouched.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShortNameLeavesBareNameAlone() =>
        await Assert.That(XmlDocMarkdownHelper.ShortName("Foo".AsSpan()).ToString()).IsEqualTo("Foo");

    /// <summary>An empty span yields an empty markdown string.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertSpanToMarkdownReturnsEmptyForEmptySpan() =>
        await Assert.That(XmlDocMarkdownHelper.ConvertSpanToMarkdown(default)).IsEqualTo(string.Empty);

    /// <summary>Plain text bypasses the scanner -- entities are still decoded.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertSpanToMarkdownDecodesPlainTextEntities() =>
        await Assert.That(XmlDocMarkdownHelper.ConvertSpanToMarkdown("a &lt;b&gt; c".AsSpan())).IsEqualTo("a <b> c");

    /// <summary>Tagged input goes through the full scanner walk.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertSpanToMarkdownRunsScannerForTaggedInput()
    {
        var rendered = XmlDocMarkdownHelper.ConvertSpanToMarkdown("<c>x</c>".AsSpan());
        await Assert.That(rendered).IsEqualTo("`x`");
    }

    /// <summary>Trailing whitespace is trimmed off the buffer.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TrimTrailingWhitespaceClipsTrailingSpaces()
    {
        var sb = new StringBuilder("hello   \n\t");
        XmlDocMarkdownHelper.TrimTrailingWhitespace(sb);
        await Assert.That(sb.ToString()).IsEqualTo("hello");
    }

    /// <summary>EnsureBlankLine appends two newlines when the buffer has content.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnsureBlankLineAppendsBlankLineWhenContent()
    {
        var sb = new StringBuilder("line ");
        XmlDocMarkdownHelper.EnsureBlankLine(sb);
        await Assert.That(sb.ToString()).IsEqualTo("line\n\n");
    }

    /// <summary>EnsureBlankLine on an empty buffer leaves it empty.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnsureBlankLineNoOpOnEmptyBuffer()
    {
        var sb = new StringBuilder();
        XmlDocMarkdownHelper.EnsureBlankLine(sb);
        await Assert.That(sb.Length).IsEqualTo(0);
    }

    /// <summary>EnsureLineStart appends a single newline when the buffer has content.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnsureLineStartAppendsNewlineWhenContent()
    {
        var sb = new StringBuilder("a   ");
        XmlDocMarkdownHelper.EnsureLineStart(sb);
        await Assert.That(sb.ToString()).IsEqualTo("a\n");
    }

    /// <summary>CollapseWhitespace folds runs of spaces and tabs into a single space and trims the tail.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CollapseWhitespaceFoldsRuns()
    {
        var sb = new StringBuilder("a   b\t\tc   ");
        XmlDocMarkdownHelper.CollapseWhitespace(sb);
        await Assert.That(sb.ToString()).IsEqualTo("a b c");
    }

    /// <summary>CollapseWhitespace leaves embedded newlines alone.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CollapseWhitespacePreservesNewlines()
    {
        var sb = new StringBuilder("a   b\n\nc");
        XmlDocMarkdownHelper.CollapseWhitespace(sb);
        await Assert.That(sb.ToString()).IsEqualTo("a b\n\nc");
    }

    /// <summary>EnsureLineStart on an empty buffer is a no-op (covers the early-return branch).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnsureLineStartNoOpOnEmptyBuffer()
    {
        var sb = new StringBuilder();
        XmlDocMarkdownHelper.EnsureLineStart(sb);
        await Assert.That(sb.Length).IsEqualTo(0);
    }

    /// <summary>ShortName returns single-character input unchanged -- the kind-prefix strip is skipped when length is below the prefix length.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShortNameLeavesShortInputAlone() =>
        await Assert.That(XmlDocMarkdownHelper.ShortName("X".AsSpan()).ToString()).IsEqualTo("X");

    /// <summary>ShortName treats a 2-char input without the ':' separator as a plain name (no prefix strip).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShortNameLeavesTwoCharInputWithoutColonAlone() =>
        await Assert.That(XmlDocMarkdownHelper.ShortName("Tx".AsSpan()).ToString()).IsEqualTo("Tx");
}
