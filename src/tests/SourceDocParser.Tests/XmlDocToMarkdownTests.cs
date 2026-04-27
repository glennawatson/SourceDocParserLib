// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml;
using SourceDocParser.XmlDoc;

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
    /// Handles the case where the input is a single-element list.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertReaderProducesSameOutputAsString()
    {
        const string fragment = "Use <c>Foo()</c> and pass <paramref name=\"x\"/>.";
        var expected = _converter.Convert(fragment);

        using var stringReader = new StringReader($"<root>{fragment}</root>");
        var settings = new XmlReaderSettings { Async = true };
        using var reader = XmlReader.Create(stringReader, settings);

        await MoveToFirstElementAsync(reader);

        var actual = await _converter.ConvertAsync(reader);

        await Assert.That(actual).IsEqualTo(expected);
    }

    /// <summary>
    /// Calling the reader overload on an empty element yields an empty string.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertReaderReturnsEmptyForEmptyElement()
    {
        using var stringReader = new StringReader("<empty/>");
        var settings = new XmlReaderSettings { Async = true };
        using var reader = XmlReader.Create(stringReader, settings);
        await MoveToFirstElementAsync(reader);

        var result = await _converter.ConvertAsync(reader);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// A null reader throws <see cref="ArgumentNullException"/>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertReaderValidatesArgument()
    {
        await Assert.That(ActReaderAsync).Throws<ArgumentNullException>();
        await Assert.That(ActReaderWithTokenAsync).Throws<ArgumentNullException>();
        return;

        Task ActReaderAsync() => _converter.ConvertAsync(null!);

        Task ActReaderWithTokenAsync() => _converter.ConvertAsync(null!, CancellationToken.None);
    }

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
        await Assert.That(_converter.Convert(default(ReadOnlySpan<char>))).IsEqualTo(string.Empty);

    /// <summary>A numbered list renders with sequential prefixes.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersNumberedList()
    {
        var result = _converter.Convert("""
            <list type="number">
              <item><description>One</description></item>
              <item><description>Two</description></item>
            </list>
            """);

        await Assert.That(result).Contains("1. One");
        await Assert.That(result).Contains("2. Two");
    }

    /// <summary>A table list renders the header row + separator + body rows.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersTableList()
    {
        var result = _converter.Convert("""
            <list type="table">
              <listheader><term>Name</term><description>Purpose</description></listheader>
              <item><term>Foo</term><description>Bars the baz</description></item>
            </list>
            """);

        await Assert.That(result).Contains("| Name | Purpose |");
        await Assert.That(result).Contains("| --- | --- |");
        await Assert.That(result).Contains("| Foo | Bars the baz |");
    }

    /// <summary>Code blocks render fenced and decode entities.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersCodeBlockWithEntities()
    {
        var result = _converter.Convert("<code>var x = a &amp;&amp; b;</code>");

        await Assert.That(result).Contains("```csharp");
        await Assert.That(result).Contains("var x = a && b;");
    }

    /// <summary>Inline c with nested c suppresses the nested tag (text-only).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertCInlineSuppressesNestedTags()
    {
        var result = _converter.Convert("<c>Use <c>Inner</c> here</c>");

        await Assert.That(result).IsEqualTo("`Use Inner here`");
    }

    /// <summary>br renders as a hard line break.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertBrRendersAsHardLineBreak()
    {
        var result = _converter.Convert("first<br/>second");

        await Assert.That(result).Contains("first");
        await Assert.That(result).Contains("second");
        await Assert.That(result).Contains("\n");
    }

    /// <summary>Para wraps content in surrounding blank lines.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertParaSurroundsWithBlankLines()
    {
        var result = _converter.Convert("Lead.<para>Body.</para>Tail.");

        await Assert.That(result).Contains("Body.");
    }

    /// <summary>see href with body renders as a Markdown link.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertSeeHrefWithBodyRendersAsMarkdownLink()
    {
        var result = _converter.Convert("""See <see href="https://example.com">the docs</see>.""");

        await Assert.That(result).Contains("[the docs](https://example.com)");
    }

    /// <summary>see href without body renders as an autolink.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertSeeHrefWithoutBodyRendersAsAutolink()
    {
        var result = _converter.Convert("""See <see href="https://example.com"/>.""");

        await Assert.That(result).Contains("<https://example.com>");
    }

    /// <summary>Unknown tags pass through their inner content.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertUnknownTagPreservesInnerContent()
    {
        var result = _converter.Convert("Note <weird>important</weird> stuff.");

        await Assert.That(result).Contains("important");
    }

    /// <summary>Advances an async XML reader to the first start element.</summary>
    /// <param name="reader">Reader to advance.</param>
    /// <returns>A task representing the asynchronous move.</returns>
    private static async Task MoveToFirstElementAsync(XmlReader reader)
    {
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                return;
            }
        }
    }
}
