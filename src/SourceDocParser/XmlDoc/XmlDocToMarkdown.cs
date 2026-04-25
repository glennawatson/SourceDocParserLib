// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using System.Xml;

namespace SourceDocParser;

/// <summary>
/// Converts .NET XML documentation fragments into Markdown.
/// </summary>
/// <remarks>
/// Driven by DocXmlScanner — a span-based forward scanner — instead of
/// XmlReader, so the conversion path no longer allocates an
/// XmlTextReaderImpl with its multi-KB buffers per call. Handles the
/// standard inline doc tags: see, seealso, paramref, typeparamref, c,
/// code, para, b/strong, i/em, br, list (bullet/number/table), item,
/// term, description. Unknown tags fall through to their inner content
/// so nothing is silently dropped.
/// </remarks>
public sealed class XmlDocToMarkdown : IXmlDocToMarkdownConverter
{
    /// <summary>Initial StringBuilder capacity for tagged conversions; trimmed back as needed.</summary>
    private const int InitialBuilderCapacity = 256;

    /// <summary>
    /// Pooled StringBuilder reused across top-level Convert calls so each
    /// per-symbol render doesn't allocate a fresh one. Convert() calls are
    /// always sequential within a single converter instance because each
    /// DocResolver creates its own (DocResolver itself is per-walk and
    /// single-threaded), so no synchronisation is needed.
    /// </summary>
    private readonly StringBuilder _builder = new(InitialBuilderCapacity);

    /// <inheritdoc />
    public string Convert(string xmlFragment) => Convert(xmlFragment.AsSpan());

    /// <inheritdoc />
    public string Convert(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.IsEmptyElement)
        {
            return string.Empty;
        }

        // Materialise the inner XML once and route through the scanner-
        // based renderer. Callers that already have an XmlReader open
        // (rare since DocResolver moved to the scanner) pay one extra
        // string allocation; the alternative — keeping a parallel
        // XmlReader-based renderer — is more code with no real win.
        return Convert(reader.ReadInnerXml().AsSpan());
    }

    /// <inheritdoc />
    public string Convert(ReadOnlySpan<char> innerXml)
    {
        if (innerXml.IsEmpty || innerXml.IsWhiteSpace())
        {
            return string.Empty;
        }

        var sb = _builder;
        sb.Clear();

        // Plain-text fast path: no '<' means no inline tags to render,
        // just decode standard entities. Skips even the scanner overhead.
        if (innerXml.IndexOf('<') < 0)
        {
            DocXmlScanner.AppendDecoded(sb, innerXml);
            return sb.ToString();
        }

        var scanner = new DocXmlScanner(innerXml);
        WriteFragment(ref scanner, sb, ListContext.None);
        return CollapseWhitespace(sb).ToString();
    }

    /// <summary>
    /// Top-level walk over the fragment. Reads tokens until the input
    /// is exhausted; treats every start element as a renderable child.
    /// </summary>
    /// <param name="scanner">Scanner over the fragment text.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="listContext">Inherited list context.</param>
    private static void WriteFragment(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext)
    {
        while (scanner.Read())
        {
            switch (scanner.Kind)
            {
                case DocTokenKind.Text:
                    {
                        DocXmlScanner.AppendDecoded(sb, scanner.RawText);
                        break;
                    }

                case DocTokenKind.StartElement:
                    {
                        WriteElement(ref scanner, sb, listContext);
                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Walks the children of the current element until the matching end
    /// element. Caller must invoke immediately after processing a start
    /// element token (so scanner.Depth is the element's open depth).
    /// </summary>
    /// <param name="scanner">Scanner positioned just after a start element.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="listContext">Inherited list context.</param>
    /// <param name="suppressTags">When true, nested elements emit text-only without markdown formatting.</param>
    private static void WriteSubtreeChildren(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext, bool suppressTags)
    {
        var startDepth = scanner.Depth;
        while (scanner.Read())
        {
            if (scanner.Kind == DocTokenKind.EndElement && scanner.Depth < startDepth)
            {
                return;
            }

            switch (scanner.Kind)
            {
                case DocTokenKind.Text:
                    {
                        DocXmlScanner.AppendDecoded(sb, scanner.RawText);
                        break;
                    }

                case DocTokenKind.StartElement:
                    {
                        if (suppressTags && !scanner.IsEmptyElement)
                        {
                            WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: true);
                        }
                        else if (!suppressTags)
                        {
                            WriteElement(ref scanner, sb, listContext);
                        }

                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Dispatches the current start element to its renderer.
    /// </summary>
    /// <param name="scanner">Scanner positioned on a start element.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="listContext">Inherited list context.</param>
    private static void WriteElement(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext)
    {
        var name = scanner.Name;
        if (name.SequenceEqual("see") || name.SequenceEqual("seealso"))
        {
            WriteSee(ref scanner, sb);
        }
        else if (name.SequenceEqual("paramref") || name.SequenceEqual("typeparamref"))
        {
            var refName = scanner.GetAttribute("name");
            if (refName.Length > 0)
            {
                sb.Append('`').Append(refName).Append('`');
            }

            scanner.SkipElement();
        }
        else if (name.SequenceEqual("c"))
        {
            sb.Append('`');
            WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: true);
            sb.Append('`');
        }
        else if (name.SequenceEqual("code"))
        {
            WriteCode(ref scanner, sb);
        }
        else if (name.SequenceEqual("para"))
        {
            EnsureBlankLine(sb);
            WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: false);
            EnsureBlankLine(sb);
        }
        else if (name.SequenceEqual("br"))
        {
            sb.Append("  \n");
            scanner.SkipElement();
        }
        else if (name.SequenceEqual("b") || name.SequenceEqual("strong"))
        {
            sb.Append("**");
            WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: false);
            sb.Append("**");
        }
        else if (name.SequenceEqual("i") || name.SequenceEqual("em"))
        {
            sb.Append('*');
            WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: false);
            sb.Append('*');
        }
        else if (name.SequenceEqual("list"))
        {
            WriteList(ref scanner, sb);
        }
        else if (name.SequenceEqual("item"))
        {
            // Bare item outside <list>: render as a bullet so we don't
            // lose the content even if the doc author was sloppy with
            // the wrapping element.
            EnsureLineStart(sb);
            sb.Append("- ");
            WriteSubtreeChildren(ref scanner, sb, ListContext.Bullet, suppressTags: false);
            EnsureLineStart(sb);
        }
        else if (name.SequenceEqual("description") || name.SequenceEqual("term"))
        {
            // List item parts: emit children inline; the parent list
            // writer arranges separators.
            WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: false);
        }
        else
        {
            // Unknown tag: keep its content so we don't drop words.
            WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: false);
        }
    }

    /// <summary>
    /// Renders a see or seealso reference. cref → markdown-style link;
    /// langword → inline code; href → autolink or hyperlink.
    /// </summary>
    /// <param name="scanner">Scanner positioned on the see/seealso start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    private static void WriteSee(ref DocXmlScanner scanner, StringBuilder sb)
    {
        var cref = scanner.GetAttribute("cref");
        if (cref.Length > 0)
        {
            sb.Append('[').Append(ShortName(cref)).Append("][").Append(cref).Append(']');
            scanner.SkipElement();
            return;
        }

        var langword = scanner.GetAttribute("langword");
        if (langword.Length > 0)
        {
            sb.Append('`').Append(langword).Append('`');
            scanner.SkipElement();
            return;
        }

        var href = scanner.GetAttribute("href");
        if (href.Length > 0)
        {
            if (scanner.IsEmptyElement)
            {
                sb.Append('<').Append(href).Append('>');
                return;
            }

            sb.Append('[');
            WriteSubtreeChildren(ref scanner, sb, ListContext.None, suppressTags: true);
            sb.Append("](").Append(href).Append(')');
            return;
        }

        scanner.SkipElement();
    }

    /// <summary>
    /// Renders a code block as fenced C#. The body is captured as a raw
    /// inner span and entity-decoded so embedded angle brackets survive.
    /// </summary>
    /// <param name="scanner">Scanner positioned on the code start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    private static void WriteCode(ref DocXmlScanner scanner, StringBuilder sb)
    {
        if (scanner.IsEmptyElement)
        {
            return;
        }

        var inner = scanner.ReadInnerSpan();
        EnsureBlankLine(sb);
        sb.Append("```csharp\n");
        var trimmed = inner.Trim();
        DocXmlScanner.AppendDecoded(sb, trimmed);
        sb.Append("\n```\n");
    }

    /// <summary>
    /// Renders a list element based on its type attribute (bullet,
    /// number, or table — bullet by default).
    /// </summary>
    /// <param name="scanner">Scanner positioned on the list start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    private static void WriteList(ref DocXmlScanner scanner, StringBuilder sb)
    {
        if (scanner.IsEmptyElement)
        {
            return;
        }

        var type = scanner.GetAttribute("type");
        EnsureBlankLine(sb);

        if (type.Equals("table", StringComparison.OrdinalIgnoreCase))
        {
            WriteListAsTable(ref scanner, sb);
        }
        else
        {
            var numbered = type.Equals("number", StringComparison.OrdinalIgnoreCase);
            WriteListAsBulletsOrNumbered(ref scanner, sb, numbered);
        }

        EnsureBlankLine(sb);
    }

    /// <summary>
    /// Renders bullet or numbered list items by walking each child item
    /// element and recursing into its children.
    /// </summary>
    /// <param name="scanner">Scanner positioned on the list start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="numbered">True for numbered, false for bullet.</param>
    private static void WriteListAsBulletsOrNumbered(ref DocXmlScanner scanner, StringBuilder sb, bool numbered)
    {
        var listDepth = scanner.Depth;
        var index = 1;
        while (scanner.Read())
        {
            if (scanner.Kind == DocTokenKind.EndElement && scanner.Depth < listDepth)
            {
                return;
            }

            if (scanner.Kind != DocTokenKind.StartElement || !scanner.Name.SequenceEqual("item"))
            {
                continue;
            }

            EnsureLineStart(sb);
            if (numbered)
            {
                sb.Append(index++).Append(". ");
            }
            else
            {
                sb.Append("- ");
            }

            WriteSubtreeChildren(ref scanner, sb, numbered ? ListContext.Numbered : ListContext.Bullet, suppressTags: false);
            sb.Append('\n');
        }
    }

    /// <summary>
    /// Renders a list as a Markdown table — listheader supplies the
    /// header row, item rows fill it in. A default "Term | Description"
    /// header is emitted if no listheader is present.
    /// </summary>
    /// <param name="scanner">Scanner positioned on the list start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    private static void WriteListAsTable(ref DocXmlScanner scanner, StringBuilder sb)
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

            if (scanner.Name.SequenceEqual("listheader"))
            {
                var inner = scanner.ReadInnerSpan();
                var (term, description) = ReadTermAndDescription(inner);
                sb.Append("| ").Append(term).Append(" | ").Append(description).Append(" |\n")
                    .Append("| --- | --- |\n");
                headerWritten = true;
            }
            else if (scanner.Name.SequenceEqual("item"))
            {
                if (!headerWritten)
                {
                    sb.Append("| Term | Description |\n")
                        .Append("| --- | --- |\n");
                    headerWritten = true;
                }

                var inner = scanner.ReadInnerSpan();
                var (term, description) = ReadTermAndDescription(inner);
                sb.Append("| ").Append(term).Append(" | ").Append(description).Append(" |\n");
            }
        }
    }

    /// <summary>
    /// Pulls the term and description children out of an item or
    /// listheader's inner span. Each is converted via the same
    /// scanner-based renderer (recursively) and then escaped for
    /// table-cell context.
    /// </summary>
    /// <param name="inner">Inner XML span of one item or listheader.</param>
    /// <returns>Tuple of (term, description). Each defaults to a single space when empty so the table cell is non-collapsing.</returns>
    private static (string Term, string Description) ReadTermAndDescription(ReadOnlySpan<char> inner)
    {
        string term = string.Empty;
        string description = string.Empty;

        var scanner = new DocXmlScanner(inner);
        while (scanner.Read())
        {
            if (scanner.Kind != DocTokenKind.StartElement)
            {
                continue;
            }

            if (scanner.Name.SequenceEqual("term"))
            {
                var sub = scanner.ReadInnerSpan();
                term = TableEscape(ConvertSpanToMarkdown(sub));
            }
            else if (scanner.Name.SequenceEqual("description"))
            {
                var sub = scanner.ReadInnerSpan();
                description = TableEscape(ConvertSpanToMarkdown(sub));
            }
        }

        return (term.Length == 0 ? " " : term, description.Length == 0 ? " " : description);
    }

    /// <summary>
    /// Renders a captured inner span into Markdown via a fresh scanner.
    /// Used by the table-row helpers; equivalent to <see cref="Convert(ReadOnlySpan{char})"/>
    /// but inlined as a static helper so it doesn't need an instance.
    /// </summary>
    /// <param name="span">Inner XML span to render.</param>
    /// <returns>Markdown text.</returns>
    private static string ConvertSpanToMarkdown(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return string.Empty;
        }

        if (span.IndexOf('<') < 0)
        {
            var plain = new StringBuilder(span.Length);
            DocXmlScanner.AppendDecoded(plain, span);
            return plain.ToString();
        }

        var sb = new StringBuilder(span.Length);
        var scanner = new DocXmlScanner(span);
        WriteFragment(ref scanner, sb, ListContext.None);
        return CollapseWhitespace(sb).ToString();
    }

    /// <summary>
    /// Extracts a user-friendly short name from a Roslyn cref. Strips
    /// the kind prefix (T:/M:/P:/F:/E:), the parameter list, leading
    /// namespace + containing-type segments, and the generic-arity
    /// backtick suffix.
    /// </summary>
    /// <param name="cref">Roslyn cref string as a span.</param>
    /// <returns>The short name as a span over <paramref name="cref"/>.</returns>
    private static ReadOnlySpan<char> ShortName(ReadOnlySpan<char> cref)
    {
        var name = cref;
        if (name.Length >= 2 && name[1] == ':')
        {
            name = name[2..];
        }

        var paren = name.IndexOf('(');
        if (paren >= 0)
        {
            name = name[..paren];
        }

        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
        {
            name = name[(lastDot + 1)..];
        }

        var backtick = name.IndexOf('`');
        if (backtick >= 0)
        {
            name = name[..backtick];
        }

        return name;
    }

    /// <summary>
    /// Ensures the buffer ends with at least one blank line (two
    /// trailing line endings).
    /// </summary>
    /// <param name="sb">Buffer to inspect.</param>
    private static void EnsureBlankLine(StringBuilder sb)
    {
        TrimTrailingWhitespace(sb);
        if (sb.Length == 0)
        {
            return;
        }

        sb.Append("\n\n");
    }

    /// <summary>Ensures the buffer ends at a line start.</summary>
    /// <param name="sb">Buffer to inspect.</param>
    private static void EnsureLineStart(StringBuilder sb)
    {
        TrimTrailingWhitespace(sb);
        if (sb.Length == 0)
        {
            return;
        }

        sb.Append('\n');
    }

    /// <summary>Trims trailing whitespace from the buffer.</summary>
    /// <param name="sb">Buffer to trim.</param>
    private static void TrimTrailingWhitespace(StringBuilder sb)
    {
        while (sb.Length > 0 && char.IsWhiteSpace(sb[^1]))
        {
            sb.Length--;
        }
    }

    /// <summary>
    /// Collapses runs of spaces and tabs into single spaces, then trims
    /// trailing whitespace. Leaves embedded line breaks alone.
    /// </summary>
    /// <param name="sb">Buffer to compact.</param>
    /// <returns>The same StringBuilder instance with whitespace collapsed.</returns>
    private static StringBuilder CollapseWhitespace(StringBuilder sb)
    {
        var write = 0;
        var inSpaceRun = false;
        for (var read = 0; read < sb.Length; read++)
        {
            var ch = sb[read];
            if (ch is ' ' or '\t')
            {
                if (inSpaceRun)
                {
                    continue;
                }

                inSpaceRun = true;
                sb[write++] = ' ';
            }
            else
            {
                inSpaceRun = false;
                sb[write++] = ch;
            }
        }

        sb.Length = write;
        TrimTrailingWhitespace(sb);
        return sb;
    }

    /// <summary>
    /// Escapes pipes and replaces newlines with spaces so a string is
    /// safe to drop into a GFM table cell.
    /// </summary>
    /// <param name="text">Cell content.</param>
    /// <returns>The escaped text, or a single space when the input was empty.</returns>
    private static string TableEscape(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return " ";
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '|')
            {
                sb.Append("\\|");
            }
            else if (ch is '\n' or '\r')
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }
}
