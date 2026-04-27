// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Renders a <c>&lt;list type="table"&gt;</c> XML doc element as a
/// GFM Markdown table. Lifted out of <see cref="XmlDocToMarkdown"/>
/// so the table-row state machine reads at problem-domain level —
/// header detection (<c>&lt;listheader&gt;</c>), default-header
/// fallback when items appear first, and per-item term/description
/// extraction. Tested in isolation against XML fragments without
/// going through the full markdown converter.
/// </summary>
internal static class MarkdownListTableRenderer
{
    /// <summary>
    /// Walks <paramref name="scanner"/> from the start of a list element
    /// and emits one Markdown table to <paramref name="sb"/>. Stops at
    /// the matching list-end element. The first <c>&lt;listheader&gt;</c>
    /// child supplies the header row; if none appears before the first
    /// <c>&lt;item&gt;</c>, a default <c>Term | Description</c> header
    /// is emitted so the table is well-formed.
    /// </summary>
    /// <param name="scanner">Scanner positioned on the list start tag; advanced past the matching end tag.</param>
    /// <param name="sb">Destination buffer.</param>
    public static void Render(ref DocXmlScanner scanner, StringBuilder sb)
    {
        var listDepth = scanner.Depth;
        var headerWritten = false;
        while (scanner.Read())
        {
            if (scanner.Kind == DocTokenKind.EndElement && scanner.Depth < listDepth)
            {
                return;
            }

            if (scanner.Kind != DocTokenKind.StartElement)
            {
                continue;
            }

            switch (scanner.Name)
            {
                case "listheader":
                    {
                        var inner = scanner.ReadInnerSpan();
                        var (term, description) = ReadTermAndDescription(inner);
                        WriteHeader(sb, term, description);
                        headerWritten = true;
                        break;
                    }

                case "item":
                    {
                        if (!headerWritten)
                        {
                            WriteHeader(sb, "Term", "Description");
                            headerWritten = true;
                        }

                        var inner = scanner.ReadInnerSpan();
                        var (term, description) = ReadTermAndDescription(inner);
                        WriteRow(sb, term, description);
                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Pulls the term and description children out of an item or
    /// listheader's inner span. Each is converted through the same
    /// scanner-based markdown renderer (recursively) and then escaped
    /// for table-cell context. Empty strings collapse to a single
    /// space so the table cell remains visible.
    /// </summary>
    /// <param name="inner">Inner XML span of one item or listheader.</param>
    /// <returns>Tuple of (term, description); each defaults to a single space when empty.</returns>
    public static (string Term, string Description) ReadTermAndDescription(in ReadOnlySpan<char> inner)
    {
        var term = string.Empty;
        var description = string.Empty;

        var scanner = new DocXmlScanner(inner);
        while (scanner.Read())
        {
            if (scanner.Kind != DocTokenKind.StartElement)
            {
                continue;
            }

            switch (scanner.Name)
            {
                case "term":
                    {
                        var sub = scanner.ReadInnerSpan();
                        term = XmlDocToMarkdown.TableEscape(XmlDocToMarkdown.ConvertSpanToMarkdown(sub));
                        break;
                    }

                case "description":
                    {
                        var sub = scanner.ReadInnerSpan();
                        description = XmlDocToMarkdown.TableEscape(XmlDocToMarkdown.ConvertSpanToMarkdown(sub));
                        break;
                    }
            }
        }

        return (term is [_, ..] ? term : " ", description is [_, ..] ? description : " ");
    }

    /// <summary>Writes the header + separator rows for the table.</summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="term">Header text for the first column.</param>
    /// <param name="description">Header text for the second column.</param>
    private static void WriteHeader(StringBuilder sb, string term, string description) =>
        sb.Append("| ").Append(term).Append(" | ").Append(description).Append(" |\n")
            .Append("| --- | --- |\n");

    /// <summary>Writes one body row of the table.</summary>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="term">Body text for the first column.</param>
    /// <param name="description">Body text for the second column.</param>
    private static void WriteRow(StringBuilder sb, string term, string description) =>
        sb.Append("| ").Append(term).Append(" | ").Append(description).Append(" |\n");
}
