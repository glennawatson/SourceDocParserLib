// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Helper methods for converting XML documentation to Markdown.
/// </summary>
internal static class XmlDocMarkdownHelper
{
    /// <summary>
    /// Length of the prefix in a Roslyn cref.
    /// </summary>
    private const int CrefPrefixLength = 2;

    /// <summary>
    /// Renders a captured inner span into Markdown via a fresh scanner.
    /// </summary>
    /// <param name="span">Inner XML span to render.</param>
    /// <returns>Markdown text.</returns>
    public static string ConvertSpanToMarkdown(in ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return string.Empty;
        }

        if (span.IndexOf('<') < 0)
        {
            var plain = new StringBuilder(span.Length);
            XmlEntityDecoder.AppendDecoded(plain, span);
            return plain.ToString();
        }

        var sb = new StringBuilder(span.Length);
        var scanner = new DocXmlScanner(span);
        WriteFragment(ref scanner, sb, ListContext.None);
        return CollapseWhitespace(sb).ToString();
    }

    /// <summary>
    /// Escapes pipes and replaces newlines with spaces so a string is
    /// safe to drop into a GFM table cell.
    /// </summary>
    /// <param name="text">Cell content.</param>
    /// <returns>The escaped text, or a single space when the input was empty.</returns>
    public static string TableEscape(string text)
    {
        if (text is not [_, ..])
        {
            return " ";
        }

        if (text.AsSpan().IndexOfAny(['|', '\n', '\r']) < 0)
        {
            return text;
        }

        return string.Create(
            text.Length + CountEscapedPipes(text),
            text,
            static (dest, state) =>
            {
                var cursor = 0;
                for (var i = 0; i < state.Length; i++)
                {
                    switch (state[i])
                    {
                        case '|':
                        {
                            dest[cursor++] = '\\';
                            dest[cursor++] = '|';
                            break;
                        }

                        case '\n' or '\r':
                        {
                            dest[cursor++] = ' ';
                            break;
                        }

                        default:
                        {
                            dest[cursor++] = state[i];
                            break;
                        }
                    }
                }
            });
    }

    /// <summary>
    /// Top-level walk over the fragment. Reads tokens until the input
    /// is exhausted; treats every start element as a renderable child.
    /// </summary>
    /// <param name="scanner">Scanner over the fragment text.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="listContext">Inherited list context.</param>
    public static void WriteFragment(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext)
    {
        while (scanner.Read())
        {
            switch (scanner.Kind)
            {
                case DocTokenKind.Text:
                    {
                        XmlEntityDecoder.AppendDecoded(sb, scanner.RawText);
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
    public static void WriteSubtreeChildren(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext, bool suppressTags)
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
                        XmlEntityDecoder.AppendDecoded(sb, scanner.RawText);
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
    /// Dispatches the current start element to its renderer. Common
    /// inline tags (see/paramref/c/code/para/br) are matched in the
    /// hot path; less-common structural tags fall through to
    /// <see cref="WriteRareElement"/>. Per-symbol render is on the hot
    /// path, so the dispatch is intentionally flat — splitting it into
    /// a deeper tree of helpers shows up directly in
    /// <c>XmlDocToMarkdownBenchmarks</c>.
    /// </summary>
    /// <param name="scanner">Scanner positioned on a start element.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="listContext">Inherited list context.</param>
    public static void WriteElement(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext)
    {
        var name = scanner.Name;
        if (name is "see" or "seealso")
        {
            WriteSee(ref scanner, sb);
            return;
        }

        if (name is "paramref" or "typeparamref")
        {
            WriteParamRef(ref scanner, sb);
            return;
        }

        if (name is "c")
        {
            WriteC(ref scanner, sb, listContext);
            return;
        }

        if (name is "code")
        {
            WriteCode(ref scanner, sb);
            return;
        }

        if (name is "para")
        {
            WritePara(ref scanner, sb, listContext);
            return;
        }

        if (name is "br")
        {
            WriteBr(ref scanner, sb);
            return;
        }

        WriteRareElement(ref scanner, sb, listContext);
    }

    /// <summary>
    /// Dispatches the structural / styled tags that don't appear on
    /// the hottest path: bold, italic, list scaffolding, and the
    /// unknown-tag fallback. Kept off <see cref="WriteElement"/> so
    /// the inline-tag dispatch stays one method.
    /// </summary>
    /// <param name="scanner">Scanner positioned on a start element.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="listContext">Inherited list context.</param>
    public static void WriteRareElement(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext)
    {
        var name = scanner.Name;
        if (name is "b" or "strong")
        {
            WriteBold(ref scanner, sb, listContext);
            return;
        }

        if (name is "i" or "em")
        {
            WriteItalic(ref scanner, sb, listContext);
            return;
        }

        if (name is "list")
        {
            WriteList(ref scanner, sb);
            return;
        }

        if (name is "item")
        {
            WriteItem(ref scanner, sb);
            return;
        }

        if (name is "description" or "term")
        {
            WriteDescriptionOrTerm(ref scanner, sb, listContext);
            return;
        }

        WriteUnknown(ref scanner, sb, listContext);
    }

    /// <summary>
    /// Renders a see or seealso reference. cref → markdown-style link;
    /// langword → inline code; href → autolink or hyperlink.
    /// </summary>
    /// <param name="scanner">Scanner positioned on the see/seealso start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    public static void WriteSee(ref DocXmlScanner scanner, StringBuilder sb)
    {
        if (scanner.GetAttribute("cref") is [_, ..] cref)
        {
            sb.Append('[').Append(ShortName(cref)).Append("][").Append(cref).Append(']');
            scanner.SkipElement();
            return;
        }

        if (scanner.GetAttribute("langword") is [_, ..] langword)
        {
            sb.Append('`').Append(langword).Append('`');
            scanner.SkipElement();
            return;
        }

        if (scanner.GetAttribute("href") is [_, ..] href)
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
    public static void WriteCode(ref DocXmlScanner scanner, StringBuilder sb)
    {
        if (scanner.IsEmptyElement)
        {
            return;
        }

        var inner = scanner.ReadInnerSpan();
        EnsureBlankLine(sb);
        sb.Append("```csharp\n");
        var trimmed = inner.Trim();
        XmlEntityDecoder.AppendDecoded(sb, trimmed);
        sb.Append("\n```\n");
    }

    /// <summary>
    /// Renders a list element based on its type attribute (bullet,
    /// number, or table — bullet by default).
    /// </summary>
    /// <param name="scanner">Scanner positioned on the list start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    public static void WriteList(ref DocXmlScanner scanner, StringBuilder sb)
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
    public static void WriteListAsBulletsOrNumbered(ref DocXmlScanner scanner, StringBuilder sb, bool numbered)
    {
        var listDepth = scanner.Depth;
        var index = 1;
        while (scanner.Read())
        {
            if (scanner.Kind == DocTokenKind.EndElement && scanner.Depth < listDepth)
            {
                return;
            }

            if (scanner.Kind != DocTokenKind.StartElement || scanner.Name is not "item")
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
    /// Renders a list as a Markdown table by delegating to
    /// <see cref="MarkdownListTableRenderer.Render"/> — kept here as a
    /// thin shim so the WriteElement dispatcher still names the
    /// table path explicitly.
    /// </summary>
    /// <param name="scanner">Scanner positioned on the list start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    public static void WriteListAsTable(ref DocXmlScanner scanner, StringBuilder sb) =>
        MarkdownListTableRenderer.Render(ref scanner, sb);

    /// <summary>
    /// Extracts a user-friendly short name from a Roslyn cref. Strips
    /// the kind prefix (T:/M:/P:/F:/E:), the parameter list, leading
    /// namespace + containing-type segments, and the generic-arity
    /// backtick suffix.
    /// </summary>
    /// <param name="cref">Roslyn cref string as a span.</param>
    /// <returns>The short name as a span over <paramref name="cref"/>.</returns>
    public static ReadOnlySpan<char> ShortName(in ReadOnlySpan<char> cref)
    {
        var name = cref;
        if (name.Length >= CrefPrefixLength && name[1] == ':')
        {
            name = name[CrefPrefixLength..];
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
    public static void EnsureBlankLine(StringBuilder sb)
    {
        TrimTrailingWhitespace(sb);
        if (sb.Length is 0)
        {
            return;
        }

        sb.Append("\n\n");
    }

    /// <summary>Ensures the buffer ends at a line start.</summary>
    /// <param name="sb">Buffer to inspect.</param>
    public static void EnsureLineStart(StringBuilder sb)
    {
        TrimTrailingWhitespace(sb);
        if (sb.Length is 0)
        {
            return;
        }

        sb.Append('\n');
    }

    /// <summary>Trims trailing whitespace from the buffer.</summary>
    /// <param name="sb">Buffer to trim.</param>
    public static void TrimTrailingWhitespace(StringBuilder sb)
    {
        while (sb.Length is not 0 && char.IsWhiteSpace(sb[^1]))
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
    public static StringBuilder CollapseWhitespace(StringBuilder sb)
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

    /// <summary>Renders a paramref or typeparamref element.</summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="sb">The string builder.</param>
    private static void WriteParamRef(ref DocXmlScanner scanner, StringBuilder sb)
    {
        if (scanner.GetAttribute("name") is [_, ..] refName)
        {
            sb.Append('`').Append(refName).Append('`');
        }

        scanner.SkipElement();
    }

    /// <summary>Renders a c element.</summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="sb">The string builder.</param>
    /// <param name="listContext">The list context.</param>
    private static void WriteC(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext)
    {
        sb.Append('`');
        WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: true);
        sb.Append('`');
    }

    /// <summary>Renders a para element.</summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="sb">The string builder.</param>
    /// <param name="listContext">The list context.</param>
    private static void WritePara(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext)
    {
        EnsureBlankLine(sb);
        WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: false);
        EnsureBlankLine(sb);
    }

    /// <summary>Renders a br element.</summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="sb">The string builder.</param>
    private static void WriteBr(ref DocXmlScanner scanner, StringBuilder sb)
    {
        sb.Append("  \n");
        scanner.SkipElement();
    }

    /// <summary>Renders a b or strong element.</summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="sb">The string builder.</param>
    /// <param name="listContext">The list context.</param>
    private static void WriteBold(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext)
    {
        sb.Append("**");
        WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: false);
        sb.Append("**");
    }

    /// <summary>Renders an i or em element.</summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="sb">The string builder.</param>
    /// <param name="listContext">The list context.</param>
    private static void WriteItalic(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext)
    {
        sb.Append('*');
        WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: false);
        sb.Append('*');
    }

    /// <summary>Renders an item element.</summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="sb">The string builder.</param>
    private static void WriteItem(ref DocXmlScanner scanner, StringBuilder sb)
    {
        // Bare item outside <list>: render as a bullet so we don't
        // lose the content even if the doc author was sloppy with
        // the wrapping element.
        EnsureLineStart(sb);
        sb.Append("- ");
        WriteSubtreeChildren(ref scanner, sb, ListContext.Bullet, suppressTags: false);
        EnsureLineStart(sb);
    }

    /// <summary>Renders a description or term element.</summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="sb">The string builder.</param>
    /// <param name="listContext">The list context.</param>
    private static void WriteDescriptionOrTerm(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext) =>

        // List item parts: emit children inline; the parent list
        // writer arranges separators.
        WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: false);

    /// <summary>Renders an unknown element.</summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="sb">The string builder.</param>
    /// <param name="listContext">The list context.</param>
    private static void WriteUnknown(ref DocXmlScanner scanner, StringBuilder sb, ListContext listContext) =>

        // Unknown tag: keep its content so we don't drop words.
        WriteSubtreeChildren(ref scanner, sb, listContext, suppressTags: false);

    /// <summary>
    /// Counts pipes so the escaped table-cell length can be computed up front.
    /// </summary>
    /// <param name="text">Text to scan.</param>
    /// <returns>The number of extra escape characters required.</returns>
    private static int CountEscapedPipes(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            count += text[i] is '|' ? 1 : 0;
        }

        return count;
    }
}
