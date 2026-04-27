// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests.XmlDoc;

/// <summary>
/// Direct coverage of <see cref="XmlMarkupParser"/>: the low-level
/// span scanner that classifies one XML token (comment, CDATA,
/// processing instruction, end element, start element) and advances
/// the read cursor. Exercises the success and truncated-input paths
/// for each token and the dispatcher fall-through wired into
/// <see cref="XmlMarkupParser.ReadMarkup"/>.
/// </summary>
public class XmlMarkupParserTests
{
    /// <summary>A well-formed comment is silent and the cursor advances past <c>--&gt;</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadCommentConsumesWellFormedComment()
    {
        const string input = "<!-- hello -->after";
        var ok = XmlMarkupParser.TryReadComment(input.AsSpan(), 0, out var result);
        var newPos = result.NewPos;
        var success = result.Success;
        var silent = result.IsSilent;

        await Assert.That(ok).IsTrue();
        await Assert.That(success).IsTrue();
        await Assert.That(silent).IsTrue();
        await Assert.That(newPos).IsEqualTo("<!-- hello -->".Length);
    }

    /// <summary>An unterminated comment yields a failure result that consumes to end-of-input.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadCommentReturnsFailureForUnterminatedComment()
    {
        const string input = "<!-- never closes";
        var ok = XmlMarkupParser.TryReadComment(input.AsSpan(), 0, out var result);
        var success = result.Success;
        var newPos = result.NewPos;

        await Assert.That(ok).IsTrue();
        await Assert.That(success).IsFalse();
        await Assert.That(newPos).IsEqualTo(input.Length);
    }

    /// <summary>Non-comment input leaves the out result at default and reports false.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadCommentReturnsFalseWhenNotAComment()
    {
        var ok = XmlMarkupParser.TryReadComment("<tag/>".AsSpan(), 0, out _);
        await Assert.That(ok).IsFalse();
    }

    /// <summary>A well-formed CDATA section surfaces its raw inner text.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadCdataSurfacesInnerText()
    {
        const string input = "<![CDATA[<x>&lt;</x>]]>tail";
        var ok = XmlMarkupParser.TryReadCdata(input.AsSpan(), 0, out var result);
        var raw = result.RawText.ToString();
        var kind = result.Kind;
        var newPos = result.NewPos;

        await Assert.That(ok).IsTrue();
        await Assert.That(kind).IsEqualTo(DocTokenKind.Text);
        await Assert.That(raw).IsEqualTo("<x>&lt;</x>");
        await Assert.That(newPos).IsEqualTo(input.Length - "tail".Length);
    }

    /// <summary>An unterminated CDATA section reports failure and consumes to end-of-input.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadCdataReturnsFailureForUnterminatedSection()
    {
        const string input = "<![CDATA[never";
        var ok = XmlMarkupParser.TryReadCdata(input.AsSpan(), 0, out var result);
        var success = result.Success;
        var newPos = result.NewPos;

        await Assert.That(ok).IsTrue();
        await Assert.That(success).IsFalse();
        await Assert.That(newPos).IsEqualTo(input.Length);
    }

    /// <summary>Plain start-element input is not a CDATA section.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadCdataReturnsFalseWhenNotCdata()
    {
        var ok = XmlMarkupParser.TryReadCdata("<tag/>".AsSpan(), 0, out _);
        await Assert.That(ok).IsFalse();
    }

    /// <summary>A processing instruction is silent and advances past <c>?&gt;</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadProcessingInstructionSilentlyConsumes()
    {
        const string input = "<?xml version=\"1.0\"?>tail";
        var ok = XmlMarkupParser.TryReadProcessingInstruction(input.AsSpan(), 0, out var result);
        var success = result.Success;
        var silent = result.IsSilent;
        var newPos = result.NewPos;

        await Assert.That(ok).IsTrue();
        await Assert.That(success).IsTrue();
        await Assert.That(silent).IsTrue();
        await Assert.That(newPos).IsEqualTo(input.Length - "tail".Length);
    }

    /// <summary>An unterminated PI yields a failure result.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadProcessingInstructionFailsWhenUnterminated()
    {
        const string input = "<?xml never";
        var ok = XmlMarkupParser.TryReadProcessingInstruction(input.AsSpan(), 0, out var result);
        var success = result.Success;
        await Assert.That(ok).IsTrue();
        await Assert.That(success).IsFalse();
    }

    /// <summary>An end element captures its trimmed name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadEndElementCapturesName()
    {
        const string input = "</summary>tail";
        var ok = XmlMarkupParser.TryReadEndElement(input.AsSpan(), 0, out var result);
        var name = result.Name.ToString();
        var kind = result.Kind;
        var newPos = result.NewPos;

        await Assert.That(ok).IsTrue();
        await Assert.That(kind).IsEqualTo(DocTokenKind.EndElement);
        await Assert.That(name).IsEqualTo("summary");
        await Assert.That(newPos).IsEqualTo(input.Length - "tail".Length);
    }

    /// <summary>Whitespace inside the closing tag is trimmed off the name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadEndElementTrimsWhitespaceFromName()
    {
        const string input = "</  remarks  >";
        var ok = XmlMarkupParser.TryReadEndElement(input.AsSpan(), 0, out var result);
        var name = result.Name.ToString();

        await Assert.That(ok).IsTrue();
        await Assert.That(name).IsEqualTo("remarks");
    }

    /// <summary>An unterminated end tag returns failure.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadEndElementFailsWhenUnterminated()
    {
        var ok = XmlMarkupParser.TryReadEndElement("</summary".AsSpan(), 0, out var result);
        var success = result.Success;
        await Assert.That(ok).IsTrue();
        await Assert.That(success).IsFalse();
    }

    /// <summary>A non-end tag reports false on TryReadEndElement.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryReadEndElementReturnsFalseForStartElement()
    {
        var ok = XmlMarkupParser.TryReadEndElement("<summary>".AsSpan(), 0, out _);
        await Assert.That(ok).IsFalse();
    }

    /// <summary>A start element captures the name and an empty attribute area when no attributes are present.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadStartElementCapturesBareName()
    {
        var result = XmlMarkupParser.ReadStartElement("<para>tail".AsSpan(), 0);
        var name = result.Name.ToString();
        var attrLen = result.AttrArea.Length;
        var isEmpty = result.IsEmptyElement;

        await Assert.That(result.Kind).IsEqualTo(DocTokenKind.StartElement);
        await Assert.That(name).IsEqualTo("para");
        await Assert.That(attrLen).IsEqualTo(0);
        await Assert.That(isEmpty).IsFalse();
    }

    /// <summary>A start element splits the name and the attribute area at the first whitespace.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadStartElementSeparatesNameFromAttributes()
    {
        var result = XmlMarkupParser.ReadStartElement("<see cref=\"T:Foo\" />".AsSpan(), 0);
        var name = result.Name.ToString();
        var attrArea = result.AttrArea.ToString();
        var isEmpty = result.IsEmptyElement;

        await Assert.That(name).IsEqualTo("see");
        await Assert.That(attrArea).IsEqualTo(" cref=\"T:Foo\" ");
        await Assert.That(isEmpty).IsTrue();
    }

    /// <summary>A self-closing tag with no attributes is detected.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadStartElementDetectsSelfClosingNoAttributes()
    {
        var result = XmlMarkupParser.ReadStartElement("<br/>".AsSpan(), 0);
        var name = result.Name.ToString();
        var attrLen = result.AttrArea.Length;
        var isEmpty = result.IsEmptyElement;

        await Assert.That(name).IsEqualTo("br");
        await Assert.That(attrLen).IsEqualTo(0);
        await Assert.That(isEmpty).IsTrue();
    }

    /// <summary>An unterminated start tag yields a failure result.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadStartElementFailsWhenNoClosingBracket()
    {
        var result = XmlMarkupParser.ReadStartElement("<para".AsSpan(), 0);
        var success = result.Success;
        await Assert.That(success).IsFalse();
    }

    /// <summary>The dispatcher routes a comment through <see cref="XmlMarkupParser.TryReadComment"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadMarkupRoutesCommentToCommentReader()
    {
        var result = XmlMarkupParser.ReadMarkup("<!-- x -->".AsSpan(), 0);
        var silent = result.IsSilent;
        await Assert.That(silent).IsTrue();
    }

    /// <summary>The dispatcher routes a CDATA section to the CDATA reader.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadMarkupRoutesCdataToCdataReader()
    {
        var result = XmlMarkupParser.ReadMarkup("<![CDATA[x]]>".AsSpan(), 0);
        var kind = result.Kind;
        var raw = result.RawText.ToString();
        await Assert.That(kind).IsEqualTo(DocTokenKind.Text);
        await Assert.That(raw).IsEqualTo("x");
    }

    /// <summary>The dispatcher routes a PI to the PI reader.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadMarkupRoutesProcessingInstructionToPiReader()
    {
        var result = XmlMarkupParser.ReadMarkup("<?pi ?>".AsSpan(), 0);
        var silent = result.IsSilent;
        await Assert.That(silent).IsTrue();
    }

    /// <summary>The dispatcher routes an end tag to the end-element reader.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadMarkupRoutesEndElementToEndElementReader()
    {
        var result = XmlMarkupParser.ReadMarkup("</x>".AsSpan(), 0);
        var kind = result.Kind;
        var name = result.Name.ToString();
        await Assert.That(kind).IsEqualTo(DocTokenKind.EndElement);
        await Assert.That(name).IsEqualTo("x");
    }

    /// <summary>The dispatcher falls through to the start-element reader when nothing else matches.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadMarkupFallsThroughToStartElement()
    {
        var result = XmlMarkupParser.ReadMarkup("<para>".AsSpan(), 0);
        var kind = result.Kind;
        var name = result.Name.ToString();
        await Assert.That(kind).IsEqualTo(DocTokenKind.StartElement);
        await Assert.That(name).IsEqualTo("para");
    }
}
