// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="MarkdownListTableRenderer"/>: turns a
/// <c>&lt;list type="table"></c> XML doc element into a GFM table.
/// Header detection (<c>listheader</c>), default-header
/// fallback when items appear first, and per-item term/description
/// extraction are exercised end-to-end via
/// <see cref="XmlDocToMarkdown.Convert(string)"/> -- that's the path
/// the production dispatcher uses to land on the renderer, and
/// keeping the test there proves the dispatch wiring still works.
/// The <c>ReadTermAndDescription</c> helper has its own
/// span-level tests below since it's directly callable.
/// </summary>
public class MarkdownListTableRendererTests
{
    /// <summary>An explicit listheader supplies the header row.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ListHeaderSuppliesHeaderRow()
    {
        var converter = new XmlDocToMarkdown();

        var rendered = converter.Convert("""
            <list type="table">
              <listheader><term>Name</term><description>Notes</description></listheader>
              <item><term>Foo</term><description>Bar</description></item>
            </list>
            """);

        await Assert.That(rendered).Contains("| Name | Notes |\n| --- | --- |\n");
        await Assert.That(rendered).Contains("| Foo | Bar |");
    }

    /// <summary>Items without a listheader trigger the default <c>Term | Description</c> header.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MissingListHeaderEmitsDefaultHeader()
    {
        var converter = new XmlDocToMarkdown();

        var rendered = converter.Convert("""
            <list type="table">
              <item><term>Foo</term><description>Bar</description></item>
            </list>
            """);

        await Assert.That(rendered).Contains("| Term | Description |\n| --- | --- |\n");
        await Assert.That(rendered).Contains("| Foo | Bar |");
    }

    /// <summary>Pipe characters inside a description column are escaped so the table cell stays well-formed.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PipeInsideCellIsEscaped()
    {
        var converter = new XmlDocToMarkdown();

        var rendered = converter.Convert("""
            <list type="table">
              <item><term>Foo</term><description>a | b</description></item>
            </list>
            """);

        await Assert.That(rendered).Contains(@"| Foo | a \| b |");
    }

    /// <summary>Empty inner span returns single-space placeholders for both columns.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadTermAndDescriptionReturnsSpacePlaceholdersForEmptyInner()
    {
        var (term, description) = MarkdownListTableRenderer.ReadTermAndDescription(string.Empty.AsSpan(), DefaultCrefResolver.Instance);

        await Assert.That(term).IsEqualTo(" ");
        await Assert.That(description).IsEqualTo(" ");
    }

    /// <summary>ReadTermAndDescription pulls term + description from the inner XML span.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadTermAndDescriptionExtractsBothChildren()
    {
        const string inner = "<term>Hello</term><description>World</description>";

        var (term, description) = MarkdownListTableRenderer.ReadTermAndDescription(inner.AsSpan(), DefaultCrefResolver.Instance);

        await Assert.That(term).IsEqualTo("Hello");
        await Assert.That(description).IsEqualTo("World");
    }
}
