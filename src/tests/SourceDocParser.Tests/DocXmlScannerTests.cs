// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="DocXmlScanner"/> — the span-based forward
/// scanner that replaces XmlReader on the doc-parse hot path. The
/// scanner is a <c>ref struct</c> so each test collects observations
/// into local primitives before any <c>await</c>.
/// </summary>
public class DocXmlScannerTests
{
    /// <summary>An empty input yields no tokens.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadReturnsFalseForEmptyInput()
    {
        var (read, kind) = ProbeEmpty();
        await Assert.That(read).IsFalse();
        await Assert.That(kind).IsEqualTo(DocTokenKind.None);
    }

    /// <summary>Plain text yields a single Text token.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadProducesSingleTextTokenForPlainContent()
    {
        var (kind, text, more) = ProbePlain();
        await Assert.That(kind).IsEqualTo(DocTokenKind.Text);
        await Assert.That(text).IsEqualTo("hello world");
        await Assert.That(more).IsFalse();
    }

    /// <summary>Self-closing element produces a StartElement token with IsEmptyElement true; depth doesn't increase.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelfClosingElementSurfacesAsEmptyStart()
    {
        var (kind, name, depth, isEmpty, cref) = ProbeSelfClosing();
        await Assert.That(kind).IsEqualTo(DocTokenKind.StartElement);
        await Assert.That(isEmpty).IsTrue();
        await Assert.That(name).IsEqualTo("see");
        await Assert.That(depth).IsEqualTo(0);
        await Assert.That(cref).IsEqualTo("X");
    }

    /// <summary>Open + close pair: depth bumps on open, drops on close.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task OpenCloseTrackDepth()
    {
        var observations = ProbeDepth();
        List<(DocTokenKind, int)> expected =
        [
            (DocTokenKind.StartElement, 1),
            (DocTokenKind.Text, 1),
            (DocTokenKind.EndElement, 0),
        ];
        await Assert.That(observations).IsEquivalentTo(expected);
    }

    /// <summary>Comments are silently consumed.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CommentsAreSkipped()
    {
        var (kind, text) = ProbeFirstToken("<!-- ignored -->after");
        await Assert.That(kind).IsEqualTo(DocTokenKind.Text);
        await Assert.That(text).IsEqualTo("after");
    }

    /// <summary>CDATA sections surface as Text.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CdataSurfacesAsText()
    {
        var (kind, text) = ProbeFirstToken("<![CDATA[<not-a-tag>]]>");
        await Assert.That(kind).IsEqualTo(DocTokenKind.Text);
        await Assert.That(text).IsEqualTo("<not-a-tag>");
    }

    /// <summary>GetAttribute returns each named attribute and an empty span when absent.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeReturnsRequestedValue()
    {
        var (name, type, missingEmpty) = ProbeAttributes();
        await Assert.That(name).IsEqualTo("x");
        await Assert.That(type).IsEqualTo("int");
        await Assert.That(missingEmpty).IsTrue();
    }

    /// <summary>ReadInnerSpan captures the raw inner XML and leaves the scanner past the end tag.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadInnerSpanCapturesNestedContent()
    {
        var (inner, tail) = ProbeReadInner("<summary>Use <c>Foo()</c>.</summary>tail");
        await Assert.That(inner).IsEqualTo("Use <c>Foo()</c>.");
        await Assert.That(tail).IsEqualTo("tail");
    }

    /// <summary>ReadInnerSpan on a self-closing element yields empty without consuming further tokens.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadInnerSpanReturnsEmptyForSelfClosing()
    {
        var (inner, tail) = ProbeReadInner("<see/>after");
        await Assert.That(inner).IsEqualTo(string.Empty);
        await Assert.That(tail).IsEqualTo("after");
    }

    /// <summary>SkipElement advances past the matching end tag without yielding child tokens.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SkipElementJumpsToMatchingEnd()
    {
        var tail = ProbeSkip("<a>x<b>y</b>z</a>tail");
        await Assert.That(tail).IsEqualTo("tail");
    }

    /// <summary>IsWhitespace recognises the four characters allowed inside an XML start tag and rejects everything else.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsWhitespaceMatchesXmlSpec()
    {
        // The four whitespace characters the XML spec recognises inside a start tag.
        await Assert.That(DocXmlScanner.IsWhitespace(' ')).IsTrue();
        await Assert.That(DocXmlScanner.IsWhitespace('\t')).IsTrue();
        await Assert.That(DocXmlScanner.IsWhitespace('\r')).IsTrue();
        await Assert.That(DocXmlScanner.IsWhitespace('\n')).IsTrue();

        // Everything else is non-whitespace, including non-breaking
        // space (U+00A0) which many text-rendering layers do treat as
        // whitespace; the XML spec does not.
        await Assert.That(DocXmlScanner.IsWhitespace('a')).IsFalse();
        await Assert.That(DocXmlScanner.IsWhitespace('\v')).IsFalse();
        await Assert.That(DocXmlScanner.IsWhitespace(' ')).IsFalse();
    }

    /// <summary>A processing instruction is silently consumed and the scanner reports the trailing text token.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProcessingInstructionsAreSkipped()
    {
        var (kind, text) = ProbeFirstToken("<?xml version=\"1.0\"?>after");
        await Assert.That(kind).IsEqualTo(DocTokenKind.Text);
        await Assert.That(text).IsEqualTo("after");
    }

    /// <summary>Truncated comment (no closing <c>--&gt;</c>) yields a None token and exhausts the input.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TruncatedCommentTerminatesScanner()
    {
        var (read, kind) = ProbeFirstReadResult("<!-- never closes");
        await Assert.That(read).IsFalse();
        await Assert.That(kind).IsEqualTo(DocTokenKind.None);
    }

    /// <summary>Truncated CDATA (no closing <c>]]&gt;</c>) yields a None token.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TruncatedCdataTerminatesScanner()
    {
        var (read, kind) = ProbeFirstReadResult("<![CDATA[ never closes");
        await Assert.That(read).IsFalse();
        await Assert.That(kind).IsEqualTo(DocTokenKind.None);
    }

    /// <summary>Truncated processing instruction (no closing <c>?&gt;</c>) yields a None token.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TruncatedProcessingInstructionTerminatesScanner()
    {
        var (read, kind) = ProbeFirstReadResult("<?xml version=\"1.0\"");
        await Assert.That(read).IsFalse();
        await Assert.That(kind).IsEqualTo(DocTokenKind.None);
    }

    /// <summary>End element missing the closing <c>&gt;</c> yields a None token.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TruncatedEndElementTerminatesScanner()
    {
        var (read, kind) = ProbeFirstReadResult("</foo");
        await Assert.That(read).IsFalse();
        await Assert.That(kind).IsEqualTo(DocTokenKind.None);
    }

    /// <summary>Start element missing the closing <c>&gt;</c> yields a None token.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TruncatedStartElementTerminatesScanner()
    {
        var (read, kind) = ProbeFirstReadResult("<foo cref=\"X\"");
        await Assert.That(read).IsFalse();
        await Assert.That(kind).IsEqualTo(DocTokenKind.None);
    }

    /// <summary>GetAttribute returns empty when the value is not double-quoted (the scanner only supports the .NET-emitted quoted form).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeReturnsEmptyForUnquotedValue()
    {
        var scanner = new DocXmlScanner("<see cref=bare/>".AsSpan());
        scanner.Read();

        var cref = scanner.GetAttribute("cref").ToString();
        await Assert.That(cref).IsEqualTo(string.Empty);
    }

    /// <summary>GetAttribute returns empty when the quoted value never closes — the scanner doesn't pretend to recover an unterminated attribute.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeReturnsEmptyForUnterminatedValue()
    {
        // The trailing `/` of the self-closer would normally close the
        // tag at <…/>; but the IsEmptyElement check is byte-level and
        // the scanner consumes the final `>` first, so the attr area
        // is `cref="open` with no closing quote.
        var scanner = new DocXmlScanner("<see cref=\"open>".AsSpan());
        scanner.Read();

        var cref = scanner.GetAttribute("cref").ToString();
        await Assert.That(cref).IsEqualTo(string.Empty);
    }

    /// <summary>GetAttribute on a tag with no attribute area returns empty — the early-return fast path.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetAttributeReturnsEmptyForElementWithoutAttributes()
    {
        var scanner = new DocXmlScanner("<summary>body</summary>".AsSpan());
        scanner.Read();

        var attr = scanner.GetAttribute("cref").ToString();
        await Assert.That(attr).IsEqualTo(string.Empty);
    }

    /// <summary>ReadInnerSpan returns the empty span when the input runs out before a matching end tag.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadInnerSpanReturnsEmptyForUnclosedElement()
    {
        var scanner = new DocXmlScanner("<summary>orphan content".AsSpan());
        scanner.Read();

        var inner = scanner.ReadInnerSpan().ToString();
        await Assert.That(inner).IsEqualTo(string.Empty);
    }

    /// <summary>SkipElement is a no-op when called on a self-closing element (nothing to skip).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SkipElementIsNoOpForSelfClosing()
    {
        var scanner = new DocXmlScanner("<see/>after".AsSpan());
        scanner.Read();
        scanner.SkipElement();
        scanner.Read();
        await Assert.That(scanner.RawText.ToString()).IsEqualTo("after");
    }

    /// <summary>Synchronously runs the scanner over an empty input.</summary>
    /// <returns>The Read result and the kind after.</returns>
    private static (bool Read, DocTokenKind Kind) ProbeEmpty()
    {
        var scanner = new DocXmlScanner(default);
        return (scanner.Read(), scanner.Kind);
    }

    /// <summary>Synchronously walks a plain text input.</summary>
    /// <returns>First-token kind, captured text, and whether a second Read returned true.</returns>
    private static (DocTokenKind Kind, string Text, bool More) ProbePlain()
    {
        var scanner = new DocXmlScanner("hello world".AsSpan());
        scanner.Read();
        var kind = scanner.Kind;
        var text = scanner.RawText.ToString();
        var more = scanner.Read();
        return (kind, text, more);
    }

    /// <summary>Synchronously parses a self-closing element and snapshots its state.</summary>
    /// <returns>Snapshot of the start element's kind, name, depth, empty flag, and cref attribute.</returns>
    private static (DocTokenKind Kind, string Name, int Depth, bool IsEmpty, string Cref) ProbeSelfClosing()
    {
        var scanner = new DocXmlScanner("<see cref=\"X\"/>".AsSpan());
        scanner.Read();
        return (scanner.Kind, scanner.Name.ToString(), scanner.Depth, scanner.IsEmptyElement, scanner.GetAttribute("cref").ToString());
    }

    /// <summary>Walks an open + text + close sequence and returns kind/depth pairs.</summary>
    /// <returns>List of (kind, depth) per emitted token.</returns>
    private static List<(DocTokenKind Kind, int Depth)> ProbeDepth()
    {
        var scanner = new DocXmlScanner("<a>text</a>".AsSpan());
        List<(DocTokenKind, int)> observations = [];
        while (scanner.Read())
        {
            observations.Add((scanner.Kind, scanner.Depth));
        }

        return observations;
    }

    /// <summary>Captures the kind and text of the first token returned for the supplied input.</summary>
    /// <param name="input">Raw XML span.</param>
    /// <returns>Kind and decoded raw text.</returns>
    private static (DocTokenKind Kind, string Text) ProbeFirstToken(string input)
    {
        var scanner = new DocXmlScanner(input.AsSpan());
        scanner.Read();
        return (scanner.Kind, scanner.RawText.ToString());
    }

    /// <summary>Captures whether the first <see cref="DocXmlScanner.Read"/> succeeded plus the resulting kind.</summary>
    /// <param name="input">Raw XML span.</param>
    /// <returns>The Read result and the resulting kind.</returns>
    private static (bool Read, DocTokenKind Kind) ProbeFirstReadResult(string input)
    {
        var scanner = new DocXmlScanner(input.AsSpan());
        var read = scanner.Read();
        return (read, scanner.Kind);
    }

    /// <summary>Probes attribute lookup on the canonical param element.</summary>
    /// <returns>Snapshot of name/type values plus whether a missing attribute returns empty.</returns>
    private static (string Name, string Type, bool MissingEmpty) ProbeAttributes()
    {
        var scanner = new DocXmlScanner("<param name=\"x\" type=\"int\"/>".AsSpan());
        scanner.Read();
        return (
            scanner.GetAttribute("name").ToString(),
            scanner.GetAttribute("type").ToString(),
            scanner.GetAttribute("missing").IsEmpty);
    }

    /// <summary>Probes ReadInnerSpan + the trailing token after the consumed element.</summary>
    /// <param name="input">Raw XML span.</param>
    /// <returns>Captured inner span and the trailing text after the consumed element.</returns>
    private static (string Inner, string Tail) ProbeReadInner(string input)
    {
        var scanner = new DocXmlScanner(input.AsSpan());
        scanner.Read();
        var inner = scanner.ReadInnerSpan().ToString();
        scanner.Read();
        return (inner, scanner.RawText.ToString());
    }

    /// <summary>Probes SkipElement + the trailing token after the skipped element.</summary>
    /// <param name="input">Raw XML span.</param>
    /// <returns>Trailing text after the skipped element.</returns>
    private static string ProbeSkip(string input)
    {
        var scanner = new DocXmlScanner(input.AsSpan());
        scanner.Read();
        scanner.SkipElement();
        scanner.Read();
        return scanner.RawText.ToString();
    }
}
