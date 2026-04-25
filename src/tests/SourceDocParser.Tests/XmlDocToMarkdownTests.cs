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
        var result = XmlDocToMarkdown.Convert(fragment);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// <c>&lt;c&gt;</c> renders as inline code with backticks.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersInlineCode()
    {
        var result = XmlDocToMarkdown.Convert("Use <c>Foo()</c> to do bar.");

        await Assert.That(result).Contains("`Foo()`");
    }

    /// <summary>
    /// <c>&lt;see cref="..."/&gt;</c> renders as a Markdown autoref link.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersSeeCrefAsAutorefLink()
    {
        var result = XmlDocToMarkdown.Convert("""See <see cref="T:Namespace.MyType"/>.""");

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
        var result = XmlDocToMarkdown.Convert("""Returns <see langword="null"/> when missing.""");

        await Assert.That(result).Contains("`null`");
    }

    /// <summary>
    /// <c>&lt;paramref name="..."/&gt;</c> renders the param name as inline code.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersParamRefAsInlineCode()
    {
        var result = XmlDocToMarkdown.Convert("""When <paramref name="value"/> is null.""");

        await Assert.That(result).Contains("`value`");
    }

    /// <summary>
    /// <c>&lt;code&gt;</c> renders as a fenced csharp code block.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertRendersCodeBlockAsFencedCsharp()
    {
        var result = XmlDocToMarkdown.Convert("<code>var x = 1;</code>");

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
        var bold = XmlDocToMarkdown.Convert("This is <b>important</b>.");
        var italic = XmlDocToMarkdown.Convert("This is <i>important</i>.");

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
        var result = XmlDocToMarkdown.Convert("""
            <list type="bullet">
              <item><description>First</description></item>
              <item><description>Second</description></item>
            </list>
            """);

        await Assert.That(result).Contains("- First");
        await Assert.That(result).Contains("- Second");
    }
}
