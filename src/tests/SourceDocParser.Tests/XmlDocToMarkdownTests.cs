// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="XmlDocToMarkdown"/> — focused on the
/// element-by-element transformations the converter has to nail to
/// emit clean markdown for downstream renderers.
/// </summary>
public class XmlDocToMarkdownTests
{
    /// <summary>Shared converter instance — class is stateless.</summary>
    private readonly XmlDocToMarkdown _converter = new();

    /// <summary>
    /// A null or whitespace-only fragment converts to an empty string
    /// (so callers don't have to null-guard summaries themselves).
    /// </summary>
    /// <param name="fragment">Fragment under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("\n\n\t")]
    public async Task ConvertReturnsEmptyForBlankInput(string fragment)
    {
        var result = _converter.Convert(fragment);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// <c>&lt;c&gt;</c> renders as inline code with backticks.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersInlineCode()
    {
        var result = _converter.Convert("Use <c>Foo()</c> to do bar.");

        await Assert.That(result).Contains("`Foo()`");
    }

    /// <summary>
    /// <c>&lt;see cref="..."/&gt;</c> renders as a Markdown autoref link.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersSeeCrefAsAutorefLink()
    {
        var result = _converter.Convert("""See <see cref="T:Namespace.MyType"/>.""");

        // The output uses the ShortName as the link text and the full
        // cref as the link target — autorefs format used by Zensical.
        await Assert.That(result).Contains("[MyType][T:Namespace.MyType]");
    }

    /// <summary>
    /// <c>&lt;see langword="..."/&gt;</c> renders the langword as inline code.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersSeeLangwordAsInlineCode()
    {
        var result = _converter.Convert("""Returns <see langword="null"/> when missing.""");

        await Assert.That(result).Contains("`null`");
    }

    /// <summary>
    /// <c>&lt;paramref name="..."/&gt;</c> renders the param name as inline code.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersParamRefAsInlineCode()
    {
        var result = _converter.Convert("""When <paramref name="value"/> is null.""");

        await Assert.That(result).Contains("`value`");
    }

    /// <summary>
    /// <c>&lt;code&gt;</c> renders as a fenced csharp code block.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersCodeBlockAsFencedCsharp()
    {
        var result = _converter.Convert("<code>var x = 1;</code>");

        await Assert.That(result).Contains("```csharp");
        await Assert.That(result).Contains("var x = 1;");
        await Assert.That(result).Contains("```");
    }

    /// <summary>
    /// <c>&lt;b&gt;</c>/<c>&lt;strong&gt;</c> render as bold; <c>&lt;i&gt;</c>/<c>&lt;em&gt;</c> as italic.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersBoldAndItalic()
    {
        var bold = _converter.Convert("This is <b>important</b>.");
        var italic = _converter.Convert("This is <i>important</i>.");

        await Assert.That(bold).Contains("**important**");
        await Assert.That(italic).Contains("*important*");
    }

    /// <summary>
    /// A bullet list renders as a Markdown unordered list with leading dashes.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersBulletListAsDashes()
    {
        var result = _converter.Convert("""
            <list type="bullet">
              <item><description>First</description></item>
              <item><description>Second</description></item>
            </list>
            """);

        await Assert.That(result).Contains("- First");
        await Assert.That(result).Contains("- Second");
    }

    /// <summary>
    /// The streaming <see cref="XmlDocToMarkdown.Convert(System.Xml.XmlReader)"/>
    /// overload reads the current element's children and produces the
    /// same Markdown as the string overload — verifying the
    /// nested-XmlReader allocation fix preserved behaviour.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods",
        Justification = "Test exercises sync XmlReader directly; ReadAsync is unrelated to what's being verified.")]
    public async Task ConvertReaderProducesSameOutputAsString()
    {
        const string fragment = "Use <c>Foo()</c> and pass <paramref name=\"x\"/>.";
        var expected = _converter.Convert(fragment);

        using var stringReader = new System.IO.StringReader($"<root>{fragment}</root>");
        using var reader = System.Xml.XmlReader.Create(stringReader);

        // Position on the <root> start element.
        while (reader.Read() && reader.NodeType != System.Xml.XmlNodeType.Element)
        {
        }

        var actual = _converter.Convert(reader);

        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>
    /// Calling the reader overload on an empty element yields an empty string.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods",
        Justification = "Test exercises sync XmlReader directly; ReadAsync is unrelated to what's being verified.")]
    public async Task ConvertReaderReturnsEmptyForEmptyElement()
    {
        using var stringReader = new System.IO.StringReader("<empty/>");
        using var reader = System.Xml.XmlReader.Create(stringReader);
        while (reader.Read() && reader.NodeType != System.Xml.XmlNodeType.Element)
        {
        }

        var result = _converter.Convert(reader);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// A null reader throws <see cref="System.ArgumentNullException"/>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertReaderValidatesArgument() =>
        await Assert.That(() => _converter.Convert((System.Xml.XmlReader)null!)).Throws<System.ArgumentNullException>();

    /// <summary>
    /// The span overload returns plain decoded text on the no-tag fast
    /// path (no XmlReader allocated).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertSpanFastPathDecodesEntities()
    {
        var result = _converter.Convert("a &lt;b&gt; c".AsSpan());

        await Assert.That(result).IsEqualTo("a <b> c");
    }

    /// <summary>The span overload renders inline tags by falling back to the string renderer.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertSpanRendersInlineTags()
    {
        var result = _converter.Convert("Use <c>Foo()</c>.".AsSpan());

        await Assert.That(result).Contains("`Foo()`");
    }

    /// <summary>An empty span yields an empty string without allocation.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertSpanReturnsEmptyForEmptyInput() =>
        await Assert.That(_converter.Convert(default(System.ReadOnlySpan<char>))).IsEqualTo(string.Empty);
}
